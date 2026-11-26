using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Contracts.Orders;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Persistence;
using Shouldly;
using static Shop.IntegrationTests.Ordering.OrderingTestClient;

namespace Shop.IntegrationTests.Ordering;

[Collection(ShopApiCollection.Name)]
public sealed class OrderQueryTests(ShopApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Services
    {
        get
        {
            using var _ = factory.CreateClient(); // forces host start (and startup migration)
            return factory.Services;
        }
    }

    /// <summary>Saves an order straight through the context, without its domain events, so nothing reacts to it.</summary>
    private async Task<Order> SeedOrderAsync(
        Guid customerId, DateTime placedAtUtc, params (string Name, string Options, string? Thumb, int Quantity)[] lines)
    {
        var address = ShippingAddress.Create("Ada Lovelace", "12 Analytical Row", null, "London", "N1 9GU", "GB").Value;
        var order = Order.Place(
            $"TEST-{Guid.NewGuid():N}"[..20], customerId, "seed@test.voltlane", Guid.NewGuid(), address,
            [.. lines.Select(l => new NewOrderLine(Guid.NewGuid(), l.Name, $"SKU-{l.Name}", l.Options, l.Thumb, 10m, l.Quantity))],
            placedAtUtc).Value;
        order.ClearDomainEvents();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);
        return order;
    }

    [Fact]
    public async Task Other_customers_order_is_404()
    {
        var laptop = await SeedLaptopAsync(Services, Ct);
        using var owner = await SignInAsync(factory, Ct);
        using var stranger = await SignInAsync(factory, Ct);
        await AddToBasketAsync(owner.Client, Silver(laptop), 2, Ct);
        var (orderId, orderNumber) = await PlaceAsync(owner.Client, 2199.98m, Ct);

        var order = await JsonAsync(await owner.Client.GetAsync($"/api/ordering/orders/{orderId}", Ct), Ct);
        order.GetProperty("id").GetGuid().ShouldBe(orderId);
        order.GetProperty("orderNumber").GetString().ShouldBe(orderNumber);
        order.GetProperty("status").GetString().ShouldBe("Placed");
        order.GetProperty("subtotal").GetDecimal().ShouldBe(2199.98m);
        order.GetProperty("discount").GetDecimal().ShouldBe(0m);
        order.GetProperty("total").GetDecimal().ShouldBe(2199.98m);
        order.GetProperty("shippingAddress").GetProperty("country").GetString().ShouldBe("GB");
        var line = order.GetProperty("lines").EnumerateArray().ShouldHaveSingleItem();
        line.GetProperty("variantId").GetGuid().ShouldBe(Silver(laptop));
        line.GetProperty("productName").GetString().ShouldBe(laptop.Name);
        line.GetProperty("quantity").GetInt32().ShouldBe(2);
        line.GetProperty("unitPrice").GetDecimal().ShouldBe(1099.99m);
        line.GetProperty("lineTotal").GetDecimal().ShouldBe(2199.98m);
        order.GetProperty("history").EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("status").GetString().ShouldBe("Placed");

        var problem = await ProblemAsync(
            await stranger.Client.GetAsync($"/api/ordering/orders/{orderId}", Ct), HttpStatusCode.NotFound, Ct);
        problem.GetProperty("type").GetString().ShouldBe("ordering.order_not_found");

        var unknown = await ProblemAsync(
            await owner.Client.GetAsync($"/api/ordering/orders/{Guid.NewGuid()}", Ct), HttpStatusCode.NotFound, Ct);
        unknown.GetProperty("type").GetString().ShouldBe("ordering.order_not_found");
    }

    [Fact]
    public async Task Payment_details_name_each_line_with_its_options()
    {
        var customerId = Guid.NewGuid();
        var order = await SeedOrderAsync(
            customerId, DateTime.UtcNow, ("Cable", "", null, 2), ("Phone", "Black · 128 GB", null, 1));

        await using var scope = Services.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IOrderPaymentQuery>();
        var details = (await query.GetAsync(order.Id, Ct)).ShouldNotBeNull();
        (await query.GetAsync(Guid.NewGuid(), Ct)).ShouldBeNull();

        details.OrderNumber.ShouldBe(order.OrderNumber);
        details.CustomerId.ShouldBe(customerId);
        details.CustomerEmail.ShouldBe("seed@test.voltlane");
        details.Total.ShouldBe(30m);
        details.Currency.ShouldBe("USD");
        details.Lines.ShouldBe([new("Cable", 2, 10m), new("Phone (Black · 128 GB)", 1, 10m)]);
    }

    [Fact]
    public async Task My_orders_are_paged_newest_first()
    {
        using var shopper = await SignInAsync(factory, Ct);
        var t0 = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
        var oldest = await SeedOrderAsync(shopper.UserId, t0, ("Cable", "", null, 1));
        var newest = await SeedOrderAsync(
            shopper.UserId, t0.AddDays(2), ("Phone", "", "https://img/phone.webp", 2), ("Case", "", "https://img/case.webp", 1));
        var middle = await SeedOrderAsync(shopper.UserId, t0.AddDays(1), ("Mouse", "", "https://img/mouse.webp", 3));
        await SeedOrderAsync(Guid.NewGuid(), t0.AddDays(3), ("Someone else's", "", null, 1));

        var page1 = await JsonAsync(await shopper.Client.GetAsync("/api/ordering/orders?page=1&pageSize=2", Ct), Ct);
        page1.GetProperty("totalCount").GetInt32().ShouldBe(3);
        page1.GetProperty("page").GetInt32().ShouldBe(1);
        page1.GetProperty("pageSize").GetInt32().ShouldBe(2);
        var items = page1.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("id").GetGuid()).ShouldBe([newest.Id, middle.Id]);
        var first = items[0];
        first.GetProperty("orderNumber").GetString().ShouldBe(newest.OrderNumber);
        first.GetProperty("placedAtUtc").GetDateTime().ShouldBe(t0.AddDays(2));
        first.GetProperty("status").GetString().ShouldBe("Placed");
        first.GetProperty("total").GetDecimal().ShouldBe(30m);
        first.GetProperty("itemCount").GetInt32().ShouldBe(3);
        first.GetProperty("firstThumbnailUrl").GetString().ShouldBe("https://img/phone.webp");

        var page2 = await JsonAsync(await shopper.Client.GetAsync("/api/ordering/orders?page=2&pageSize=2", Ct), Ct);
        var last = page2.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        last.GetProperty("id").GetGuid().ShouldBe(oldest.Id);
        last.GetProperty("firstThumbnailUrl").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);

        var defaults = await JsonAsync(await shopper.Client.GetAsync("/api/ordering/orders", Ct), Ct);
        defaults.GetProperty("pageSize").GetInt32().ShouldBe(10);
        defaults.GetProperty("items").GetArrayLength().ShouldBe(3);

        var tooBig = await ProblemAsync(
            await shopper.Client.GetAsync("/api/ordering/orders?pageSize=51", Ct), HttpStatusCode.BadRequest, Ct);
        tooBig.GetProperty("type").GetString().ShouldBe("validation.failed");
    }
}
