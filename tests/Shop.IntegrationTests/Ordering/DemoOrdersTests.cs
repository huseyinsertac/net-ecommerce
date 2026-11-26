using System.Net;
using System.Text.Json;
using Catalog.Infrastructure.Persistence;
using Identity.Contracts.Events;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Contracts.Events;
using Ordering.Infrastructure.Persistence;
using Shouldly;
using static Shop.IntegrationTests.Ordering.OrderingTestClient;

namespace Shop.IntegrationTests.Ordering;

[Collection(ShopApiCollection.Name)]
public sealed class DemoOrdersTests(ShopApiFactory factory)
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

    /// <summary>Makes sure at least four featured Active products exist, so the sample is full.</summary>
    private async Task SeedFeaturedCatalogAsync()
    {
        for (var i = 0; i < 4; i++)
        {
            await SeedLaptopAsync(Services, Ct);
        }
    }

    private async Task<(HttpClient Client, Guid UserId)> SignInDemoAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/identity/demo/customer", null, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        AuthTestClient.Bearer(client, doc.RootElement.GetProperty("accessToken").GetString()!);
        return (client, doc.RootElement.GetProperty("user").GetProperty("id").GetGuid());
    }

    private static async Task<List<JsonElement>> ListOrdersAsync(HttpClient client)
    {
        var body = await JsonAsync(await client.GetAsync("/api/ordering/orders", Ct), Ct);
        return [.. body.GetProperty("items").EnumerateArray()];
    }

    /// <summary>Polls (no fixed sleep) until the customer has <paramref name="count"/> orders, giving up after 5 seconds.</summary>
    private static async Task<List<JsonElement>> WaitForOrdersAsync(HttpClient client, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var orders = await ListOrdersAsync(client);
        while (orders.Count < count && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, Ct);
            orders = await ListOrdersAsync(client);
        }

        return orders;
    }

    [Fact]
    public async Task Demo_customer_gets_three_historical_orders_within_5_seconds()
    {
        await SeedFeaturedCatalogAsync();
        var before = DateTime.UtcNow;
        var (client, userId) = await SignInDemoAsync();
        using var _ = client;

        var orders = await WaitForOrdersAsync(client, 3);

        orders.Select(o => o.GetProperty("status").GetString()).ShouldBe(["Shipped", "Delivered", "Delivered"]);
        var details = new List<JsonElement>();
        foreach (var o in orders)
        {
            details.Add(await JsonAsync(await client.GetAsync($"/api/ordering/orders/{o.GetProperty("id").GetGuid()}", Ct), Ct));
        }

        var (c, b, a) = (details[0], details[1], details[2]);
        a.GetProperty("lines").GetArrayLength().ShouldBe(1);
        b.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("quantity").GetInt32()).ShouldBe([1, 2]);
        c.GetProperty("lines").GetArrayLength().ShouldBe(1);

        string[] delivered = ["Placed", "Paid", "Shipped", "Delivered"];
        double[] deliveredOffsets = [0, 1d / 24, 1, 4];
        AssertHistory(a, 41, delivered, deliveredOffsets, before);
        AssertHistory(b, 12, delivered, deliveredOffsets, before);
        AssertHistory(c, 2, ["Placed", "Paid", "Shipped"], [0, 1d / 24, 1], before);

        foreach (var order in details)
        {
            var address = order.GetProperty("shippingAddress");
            address.GetProperty("fullName").GetString().ShouldBe("Demo Customer");
            address.GetProperty("line1").GetString().ShouldBe("221 Market Street");
            address.GetProperty("city").GetString().ShouldBe("San Francisco");
            address.GetProperty("postalCode").GetString().ShouldBe("94105");
            address.GetProperty("country").GetString().ShouldBe("US");
            order.GetProperty("orderNumber").GetString()!.ShouldStartWith("VL-");
        }

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        (await db.Orders.Where(o => o.CustomerId == userId).AllAsync(o => o.IsHistorical, Ct)).ShouldBeTrue();
    }

    private static void AssertHistory(JsonElement order, int daysAgo, string[] statuses, double[] offsetDays, DateTime now)
    {
        var history = order.GetProperty("history").EnumerateArray().ToList();
        history.Select(h => h.GetProperty("status").GetString()).ShouldBe(statuses);
        var placed = history[0].GetProperty("atUtc").GetDateTime().ToUniversalTime();
        placed.ShouldBe(now.AddDays(-daysAgo), TimeSpan.FromMinutes(5));
        for (var i = 0; i < history.Count; i++)
        {
            var at = history[i].GetProperty("atUtc").GetDateTime().ToUniversalTime();
            (at - placed).TotalDays.ShouldBe(offsetDays[i], 1e-6);
        }
    }

    [Fact]
    public async Task Historical_orders_do_not_touch_stock_or_publish_OrderPlaced()
    {
        await SeedFeaturedCatalogAsync();
        var before = await StockTotalsAsync();

        var (client, userId) = await SignInDemoAsync();
        using var _ = client;
        (await WaitForOrdersAsync(client, 3)).Count.ShouldBe(3);

        (await StockTotalsAsync()).ShouldBe(before);
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var type = typeof(OrderPlaced).FullName;
        var placed = await db.OutboxMessages.AsNoTracking().Where(m => m.Type == type).Select(m => m.Content).ToListAsync(Ct);
        placed.Count(c => c.Contains(userId.ToString(), StringComparison.OrdinalIgnoreCase)).ShouldBe(0);
    }

    private async Task<(long OnHand, long Reserved)> StockTotalsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var onHand = await db.Database
            .SqlQuery<long>($"SELECT COALESCE(SUM(stock_on_hand), 0)::bigint AS \"Value\" FROM catalog.product_variants")
            .SingleAsync(Ct);
        var reserved = await db.Database
            .SqlQuery<long>($"SELECT COALESCE(SUM(stock_reserved), 0)::bigint AS \"Value\" FROM catalog.product_variants")
            .SingleAsync(Ct);
        return (onHand, reserved);
    }

    [Fact]
    public async Task Registered_customer_gets_no_historical_orders()
    {
        await SeedFeaturedCatalogAsync();
        using var shopper = await SignInAsync(factory, Ct);
        // A demo sign-in proves the seeding pipeline is live, so "no orders" is not just "too early".
        var (demo, _) = await SignInDemoAsync();
        using var __ = demo;
        (await WaitForOrdersAsync(demo, 3)).Count.ShouldBe(3);

        (await ListOrdersAsync(shopper.Client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task DemoCustomerCreated_is_in_the_outbox_once_even_when_the_email_retry_path_runs()
    {
        // There is no seam to force the random-email collision without a production hook, so this asserts the invariant
        // the retry relies on: exactly one DemoCustomerCreated row per created user. A failed save clears the change
        // tracker (UserRepository), which drops the tracked outbox row, so a retry cannot save the event twice.
        var users = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var (client, userId) = await SignInDemoAsync();
            client.Dispose();
            users.Add(userId);
        }

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var type = typeof(DemoCustomerCreated).FullName;
        var rows = await db.OutboxMessages.AsNoTracking().Where(m => m.Type == type).Select(m => m.Content).ToListAsync(Ct);
        foreach (var id in users)
        {
            rows.Count(c => c.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
        }
    }
}
