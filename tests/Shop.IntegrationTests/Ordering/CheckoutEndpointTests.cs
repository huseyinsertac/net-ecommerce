using System.Net;
using System.Text.Json;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Contracts.Events;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Persistence;
using Shouldly;
using static Shop.IntegrationTests.Ordering.OrderingTestClient;

namespace Shop.IntegrationTests.Ordering;

[Collection(ShopApiCollection.Name)]
public sealed class CheckoutEndpointTests(ShopApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Services
    {
        get
        {
            using var _ = factory.CreateClient(); // forces host start (and startup migration)
            return factory.Services;
        }
    }

    private async Task<List<Order>> OrdersOfAsync(Guid customerId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        return await db.Orders.AsNoTracking().Include(o => o.Lines).Where(o => o.CustomerId == customerId).ToListAsync(Ct);
    }

    private async Task<List<OrderPlaced>> OrderPlacedOutboxAsync(Guid orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var type = typeof(OrderPlaced).FullName;
        var rows = await db.OutboxMessages.AsNoTracking().Where(m => m.Type == type).ToListAsync(Ct);
        return [.. rows.Select(r => JsonSerializer.Deserialize<OrderPlaced>(r.Content, Json)!).Where(e => e.OrderId == orderId)];
    }

    private async Task SetStockAsync(Guid variantId, int stockOnHand)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await db.Database.ExecuteSqlAsync(
            $"UPDATE catalog.product_variants SET stock_on_hand = {stockOnHand} WHERE id = {variantId}", Ct);
    }

    [Fact]
    public async Task Checkout_creates_a_Placed_order_and_an_OrderPlaced_outbox_row()
    {
        var laptop = await SeedLaptopAsync(Services, Ct);
        using var shopper = await SignInAsync(factory, Ct);
        var (client, userId, email) = shopper;
        await AddToBasketAsync(client, Silver(laptop), 2, Ct);
        await AddToBasketAsync(client, Black(laptop), 1, Ct);

        var response = await CheckoutAsync(client, Guid.NewGuid().ToString(), 3499.48m, Ct);

        var (orderId, orderNumber) = await AcceptedAsync(response, Ct);
        response.Headers.Location!.OriginalString.ShouldBe($"/api/ordering/orders/{orderId}/checkout-status");

        var order = (await OrdersOfAsync(userId)).ShouldHaveSingleItem();
        order.Id.ShouldBe(orderId);
        order.OrderNumber.ShouldBe(orderNumber);
        order.Status.ShouldBe(OrderStatus.Placed);
        order.CustomerEmail.ShouldBe(email);
        order.Total.Amount.ShouldBe(3499.48m);
        order.Subtotal.Amount.ShouldBe(3499.48m);
        order.Discount.Amount.ShouldBe(0m);
        order.ShippingAddress.ShouldBe(new ShippingAddress("Ada Lovelace", "12 Analytical Row", null, "London", "N1 9GU", "GB"));
        order.History.ShouldHaveSingleItem().Status.ShouldBe(OrderStatus.Placed);
        order.Lines.Count.ShouldBe(2);
        var silver = order.Lines.Single(l => l.VariantId == Silver(laptop));
        silver.ProductName.ShouldBe(laptop.Name);
        silver.OptionsLabel.ShouldBe("Silver · 512 GB");
        silver.UnitPrice.Amount.ShouldBe(1099.99m);
        silver.Quantity.ShouldBe(2);
        silver.LineTotal.Amount.ShouldBe(2199.98m);

        var placed = (await OrderPlacedOutboxAsync(orderId)).ShouldHaveSingleItem();
        placed.CustomerId.ShouldBe(userId);
        placed.Total.ShouldBe(3499.48m);
        placed.Currency.ShouldBe("USD");
        placed.Lines.Select(l => (l.VariantId, l.Quantity)).OrderBy(l => l.Quantity)
            .ShouldBe([(Black(laptop), 1), (Silver(laptop), 2)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_idempotency_key_returns_the_same_order(bool parallel)
    {
        var laptop = await SeedLaptopAsync(Services, Ct);
        using var shopper = await SignInAsync(factory, Ct);
        var (client, userId, email) = shopper;
        await AddToBasketAsync(client, Silver(laptop), 1, Ct);
        var key = Guid.NewGuid().ToString();

        HttpResponseMessage[] responses = parallel
            ? await Task.WhenAll(CheckoutAsync(client, key, 1099.99m, Ct), CheckoutAsync(client, key, 1099.99m, Ct))
            : [await CheckoutAsync(client, key, 1099.99m, Ct), await CheckoutAsync(client, key, 1099.99m, Ct)];

        var first = await AcceptedAsync(responses[0], Ct);
        var second = await AcceptedAsync(responses[1], Ct);
        second.ShouldBe(first);
        (await OrdersOfAsync(userId)).ShouldHaveSingleItem().Id.ShouldBe(first.OrderId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Missing_or_non_guid_idempotency_key_is_400(string? key)
    {
        var laptop = await SeedLaptopAsync(Services, Ct);
        using var shopper = await SignInAsync(factory, Ct);
        var (client, userId, email) = shopper;
        await AddToBasketAsync(client, Silver(laptop), 1, Ct);

        var problem = await ProblemAsync(await CheckoutAsync(client, key, 1099.99m, Ct), HttpStatusCode.BadRequest, Ct);

        problem.GetProperty("type").GetString().ShouldBe("ordering.missing_idempotency_key");
        (await OrdersOfAsync(userId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Empty_basket_is_409()
    {
        using var shopper = await SignInAsync(factory, Ct);
        var (client, userId, email) = shopper;

        var problem = await ProblemAsync(
            await CheckoutAsync(client, Guid.NewGuid().ToString(), 0m, Ct), HttpStatusCode.Conflict, Ct);

        problem.GetProperty("type").GetString().ShouldBe("ordering.empty_basket");
        (await OrdersOfAsync(userId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Out_of_stock_line_is_409_basket_has_issues_and_creates_no_order()
    {
        var laptop = await SeedLaptopAsync(Services, Ct, stock: 2);
        using var shopper = await SignInAsync(factory, Ct);
        var (client, userId, email) = shopper;
        await AddToBasketAsync(client, Silver(laptop), 2, Ct);
        await AddToBasketAsync(client, Black(laptop), 1, Ct);
        await SetStockAsync(Silver(laptop), 1); // sold elsewhere after it went into the basket

        var problem = await ProblemAsync(
            await CheckoutAsync(client, Guid.NewGuid().ToString(), 3499.48m, Ct), HttpStatusCode.Conflict, Ct);

        problem.GetProperty("type").GetString().ShouldBe("ordering.basket_has_issues");
        (await OrdersOfAsync(userId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Wrong_expected_total_is_409_price_changed()
    {
        var laptop = await SeedLaptopAsync(Services, Ct);
        using var shopper = await SignInAsync(factory, Ct);
        var (client, userId, email) = shopper;
        await AddToBasketAsync(client, Silver(laptop), 1, Ct);

        var problem = await ProblemAsync(
            await CheckoutAsync(client, Guid.NewGuid().ToString(), 999.99m, Ct), HttpStatusCode.Conflict, Ct);

        problem.GetProperty("type").GetString().ShouldBe("ordering.price_changed");
        (await OrdersOfAsync(userId)).ShouldBeEmpty();
    }

    public static TheoryData<string, string?> InvalidAddresses => new()
    {
        { "fullName", "" },
        { "line1", "   " },
        { "city", null },
        { "postalCode", new string('9', 21) },
        { "country", "USA" },
        { "country", "1A" },
    };

    [Theory]
    [MemberData(nameof(InvalidAddresses))]
    public async Task Invalid_address_is_400_validation_failed(string field, string? value)
    {
        var laptop = await SeedLaptopAsync(Services, Ct);
        using var shopper = await SignInAsync(factory, Ct);
        var (client, userId, email) = shopper;
        await AddToBasketAsync(client, Silver(laptop), 1, Ct);
        var address = new Dictionary<string, string?>
        {
            ["fullName"] = "Ada Lovelace",
            ["line1"] = "12 Analytical Row",
            ["city"] = "London",
            ["postalCode"] = "N1 9GU",
            ["country"] = "GB",
            [field] = value,
        };

        var problem = await ProblemAsync(
            await CheckoutAsync(client, Guid.NewGuid().ToString(), 1099.99m, Ct, address), HttpStatusCode.BadRequest, Ct);

        problem.GetProperty("type").GetString().ShouldBe("validation.failed");
        problem.GetProperty("errors").TryGetProperty($"shippingAddress.{field}", out _).ShouldBeTrue(problem.ToString());
        (await OrdersOfAsync(userId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        using var client = factory.CreateClient();

        var response = await CheckoutAsync(client, Guid.NewGuid().ToString(), 10m, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Order_numbers_are_sequential_and_prefixed()
    {
        var laptop = await SeedLaptopAsync(Services, Ct);
        using var firstShopper = await SignInAsync(factory, Ct);
        using var secondShopper = await SignInAsync(factory, Ct);
        var (first, second) = (firstShopper.Client, secondShopper.Client);
        await AddToBasketAsync(first, Silver(laptop), 1, Ct);
        await AddToBasketAsync(second, Silver(laptop), 1, Ct);

        var a = (await PlaceAsync(first, 1099.99m, Ct)).OrderNumber;
        var b = (await PlaceAsync(second, 1099.99m, Ct)).OrderNumber;

        a.ShouldMatch(@"^VL-\d{6,}$");
        b.ShouldMatch(@"^VL-\d{6,}$");
        long.Parse(a[3..]).ShouldBeGreaterThanOrEqualTo(100001);
        long.Parse(b[3..]).ShouldBe(long.Parse(a[3..]) + 1);
    }
}
