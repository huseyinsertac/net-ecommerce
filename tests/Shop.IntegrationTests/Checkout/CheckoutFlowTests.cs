using System.Net;
using System.Net.Http.Json;
using Shouldly;
using static Shop.IntegrationTests.Checkout.CheckoutFlowClient;
using static Shop.IntegrationTests.Ordering.OrderingTestClient;

namespace Shop.IntegrationTests.Checkout;

/// <summary>
/// The whole checkout choreography through the real host: Ordering places, Catalog reserves, Payments opens the session,
/// the shopper pays (simulated), and Ordering and Catalog react. Message delivery is driven by the test, step by step.
/// </summary>
public sealed class CheckoutFlowTests(CheckoutFlowApiFactory factory) : IClassFixture<CheckoutFlowApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const decimal SilverPrice = 1099.99m;

    private IServiceProvider Services
    {
        get
        {
            using var _ = factory.CreateClient(); // forces host start (and startup migration)
            return factory.Services;
        }
    }

    private static async Task PayAsync(HttpClient client, Guid orderId, bool succeed)
    {
        var response = await client.PostAsJsonAsync($"/api/payments/simulated/{orderId}", new { succeed }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Happy_path_reaches_ReadyToPay_then_Paid_and_commits_stock()
    {
        var laptop = await SeedLaptopAsync(Services, Ct, stock: 5);
        var variantId = Silver(laptop);
        using var shopper = await SignInAsync(factory, Ct);
        await AddToBasketAsync(shopper.Client, variantId, 2, Ct);
        var before = await VariantAsync(Services, laptop.Id, variantId, Ct);

        var (orderId, _) = await PlaceAsync(shopper.Client, 2 * SilverPrice, Ct);

        (await GetStatusAsync(shopper.Client, orderId, Ct)).GetProperty("state").GetString().ShouldBe("Pending");
        var ready = await WaitForStateAsync(Services, shopper.Client, orderId, "ReadyToPay", Ct);
        ready.GetProperty("paymentUrl").GetString().ShouldNotBeNullOrWhiteSpace();
        ready.GetProperty("expiresAtUtc").GetDateTime().ShouldBeGreaterThan(DateTime.UtcNow);

        await PayAsync(shopper.Client, orderId, succeed: true);

        var paid = await WaitForStateAsync(Services, shopper.Client, orderId, "Paid", Ct);
        NoPaymentUrl(paid);
        (await HistoryAsync(shopper.Client, orderId, Ct)).ShouldBe(["Placed", "Paid"]);
        var after = await VariantAsync(Services, laptop.Id, variantId, Ct);
        after.StockOnHand.ShouldBe(before.StockOnHand - 2);
        after.StockReserved.ShouldBe(before.StockReserved);
    }

    [Fact]
    public async Task Out_of_stock_at_reservation_ends_OutOfStock_and_order_is_cancelled()
    {
        var laptop = await SeedLaptopAsync(Services, Ct, stock: 1);
        var variantId = Silver(laptop);
        using var shopper = await SignInAsync(factory, Ct);
        await AddToBasketAsync(shopper.Client, variantId, 1, Ct);
        var (orderId, _) = await PlaceAsync(shopper.Client, SilverPrice, Ct);

        // Between checkout and Catalog seeing OrderPlaced, the last unit is taken.
        await ReserveDirectlyAsync(Services, laptop.Id, variantId, 1, Ct);

        var status = await WaitForStateAsync(Services, shopper.Client, orderId, "OutOfStock", Ct);
        NoPaymentUrl(status);
        var order = await JsonAsync(await shopper.Client.GetAsync($"/api/ordering/orders/{orderId}", Ct), Ct);
        order.GetProperty("status").GetString().ShouldBe("Cancelled");
        order.GetProperty("cancellationReason").GetString().ShouldBe("OutOfStock");
        (await HistoryAsync(shopper.Client, orderId, Ct)).ShouldBe(["Placed", "Cancelled"]);
        var variant = await VariantAsync(Services, laptop.Id, variantId, Ct);
        variant.StockOnHand.ShouldBe(1);
        variant.StockReserved.ShouldBe(1); // only the direct hold; the order reserved nothing
    }

    [Fact]
    public async Task Declined_payment_cancels_the_order_and_releases_stock()
    {
        var laptop = await SeedLaptopAsync(Services, Ct, stock: 3);
        var variantId = Silver(laptop);
        using var shopper = await SignInAsync(factory, Ct);
        await AddToBasketAsync(shopper.Client, variantId, 1, Ct);
        var (orderId, _) = await PlaceAsync(shopper.Client, SilverPrice, Ct);
        await WaitForStateAsync(Services, shopper.Client, orderId, "ReadyToPay", Ct);
        (await VariantAsync(Services, laptop.Id, variantId, Ct)).StockReserved.ShouldBe(1);

        await PayAsync(shopper.Client, orderId, succeed: false);

        var failed = await WaitForStateAsync(Services, shopper.Client, orderId, "Failed", Ct);
        failed.GetProperty("reason").GetString().ShouldBe("PaymentFailed");
        (await HistoryAsync(shopper.Client, orderId, Ct)).ShouldBe(["Placed", "Cancelled"]);
        var variant = await VariantAsync(Services, laptop.Id, variantId, Ct);
        variant.StockOnHand.ShouldBe(3);
        variant.StockReserved.ShouldBe(0);
    }

    [Fact]
    public async Task Abandoned_payment_expires_cancels_and_releases_stock()
    {
        var laptop = await SeedLaptopAsync(Services, Ct, stock: 2);
        var variantId = Silver(laptop);
        using var shopper = await SignInAsync(factory, Ct);
        await AddToBasketAsync(shopper.Client, variantId, 1, Ct);
        var (orderId, _) = await PlaceAsync(shopper.Client, SilverPrice, Ct);
        await WaitForStateAsync(Services, shopper.Client, orderId, "ReadyToPay", Ct);

        // Nobody pays and the session's time runs out; the real expiry job (1-second scan) then ends the payment.
        await ExpirePaymentNowAsync(Services, orderId, Ct);

        var failed = await WaitForStateAsync(Services, shopper.Client, orderId, "Failed", Ct);
        failed.GetProperty("reason").GetString().ShouldBe("PaymentExpired");
        NoPaymentUrl(failed);
        (await HistoryAsync(shopper.Client, orderId, Ct)).ShouldBe(["Placed", "Cancelled"]);
        var variant = await VariantAsync(Services, laptop.Id, variantId, Ct);
        variant.StockOnHand.ShouldBe(2);
        variant.StockReserved.ShouldBe(0);
    }

    [Fact]
    public async Task Last_unit_bought_by_two_customers_one_pays_one_is_out_of_stock()
    {
        var laptop = await SeedLaptopAsync(Services, Ct, stock: 1);
        var variantId = Silver(laptop);
        using var first = await SignInAsync(factory, Ct);
        using var second = await SignInAsync(factory, Ct);
        await AddToBasketAsync(first.Client, variantId, 1, Ct);
        await AddToBasketAsync(second.Client, variantId, 1, Ct);

        // Both checkouts are accepted: neither order has reserved the unit yet. The first one placed reserves it.
        var (firstOrder, _) = await PlaceAsync(first.Client, SilverPrice, Ct);
        var (secondOrder, _) = await PlaceAsync(second.Client, SilverPrice, Ct);

        await WaitForStateAsync(Services, first.Client, firstOrder, "ReadyToPay", Ct);
        await WaitForStateAsync(Services, second.Client, secondOrder, "OutOfStock", Ct);

        await PayAsync(first.Client, firstOrder, succeed: true);

        await WaitForStateAsync(Services, first.Client, firstOrder, "Paid", Ct);
        var variant = await VariantAsync(Services, laptop.Id, variantId, Ct);
        variant.StockOnHand.ShouldBe(0);
        variant.StockReserved.ShouldBe(0);
    }
}
