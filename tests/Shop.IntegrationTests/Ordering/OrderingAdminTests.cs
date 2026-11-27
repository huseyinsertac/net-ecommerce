using System.Net;
using System.Net.Http.Headers;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Persistence;
using Shouldly;
using static Shop.IntegrationTests.Ordering.OrderingTestClient;

namespace Shop.IntegrationTests.Ordering;

/// <summary>The admin order API end to end, signed in as an Admin with a token from the host's own issuer.</summary>
[Collection(ShopApiCollection.Name)]
public sealed class OrderingAdminTests(ShopApiFactory factory)
{
    private const string Base = "/api/ordering/admin";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Services
    {
        get
        {
            using var _ = factory.CreateClient(); // forces host start (and startup migration)
            return factory.Services;
        }
    }

    private HttpClient Admin()
    {
        var user = User.CreateDemoAdmin(Email.Create($"admin-{Guid.NewGuid():N}@x.test").Value, DateTime.UtcNow);
        var token = Services.GetRequiredService<ITokenIssuer>().Issue(user, DateTime.UtcNow).Value;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string NewNumber() => $"VL-{Random.Shared.NextInt64(100_000_000, 999_999_999)}";

    private static string NewEmail() => $"cust-{Guid.NewGuid():N}@Shop.Test";

    /// <summary>Saves an order straight through the context (no domain events), moved to <paramref name="target"/>.</summary>
    private async Task<Order> SeedAsync(string email, DateTime placedAtUtc, OrderStatus target = OrderStatus.Placed)
    {
        var address = ShippingAddress.Create("Ada Lovelace", "12 Analytical Row", null, "London", "N1 9GU", "GB").Value;
        var order = Order.Place(
            NewNumber(), Guid.NewGuid(), email, Guid.NewGuid(), address,
            [new NewOrderLine(Guid.NewGuid(), "Widget", "SKU-W", "Blue", null, 10m, 2)], placedAtUtc).Value;
        if (target >= OrderStatus.Paid) order.MarkPaid(placedAtUtc.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        if (target >= OrderStatus.Shipped) order.Ship(placedAtUtc.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        if (target >= OrderStatus.Delivered) order.Deliver(placedAtUtc.AddMinutes(3)).IsSuccess.ShouldBeTrue();
        order.ClearDomainEvents();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);
        return order;
    }

    public static TheoryData<string, string> Routes => new()
    {
        { "GET", "/orders" },
        { "GET", "/orders/{id}" },
        { "POST", "/orders/{id}/ship" },
        { "POST", "/orders/{id}/deliver" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Customer_403_anonymous_401_on_admin_routes(string method, string route)
    {
        var order = await SeedAsync(NewEmail(), DateTime.UtcNow, OrderStatus.Paid);
        var path = Base + route.Replace("{id}", order.Id.ToString(), StringComparison.Ordinal);
        HttpRequestMessage Request() => new(new HttpMethod(method), path);

        using var anonymous = factory.CreateClient();
        (await anonymous.SendAsync(Request(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var customer = await SignInAsync(factory, Ct);
        (await customer.Client.SendAsync(Request(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var admin = Admin();
        (await admin.SendAsync(Request(), Ct)).StatusCode.ShouldNotBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Lists_all_customers_orders_newest_first_and_filters_by_status()
    {
        var email = NewEmail();
        var now = DateTime.UtcNow;
        var oldest = await SeedAsync(email, now.AddHours(-3), OrderStatus.Delivered);
        var middle = await SeedAsync(email, now.AddHours(-2), OrderStatus.Paid);
        var newest = await SeedAsync(email, now.AddHours(-1));
        using var admin = Admin();
        var q = Uri.EscapeDataString(email);

        var all = await JsonAsync(await admin.GetAsync($"{Base}/orders?q={q}", Ct), Ct);
        all.GetProperty("totalCount").GetInt32().ShouldBe(3);
        var items = all.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("id").GetGuid()).ShouldBe([newest.Id, middle.Id, oldest.Id]);
        items[0].GetProperty("customerEmail").GetString().ShouldBe(email);
        items[0].GetProperty("itemCount").GetInt32().ShouldBe(2);
        items[0].GetProperty("total").GetDecimal().ShouldBe(20m);
        items[0].GetProperty("status").GetString().ShouldBe("Placed");

        var paid = await JsonAsync(await admin.GetAsync($"{Base}/orders?status=paid&q={q}", Ct), Ct);
        paid.GetProperty("items").EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldBe(middle.Id);

        var paged = await JsonAsync(await admin.GetAsync($"{Base}/orders?q={q}&page=2&pageSize=2", Ct), Ct);
        paged.GetProperty("items").EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldBe(oldest.Id);

        await ProblemAsync(await admin.GetAsync($"{Base}/orders?status=Bogus", Ct), HttpStatusCode.BadRequest, Ct);
        await ProblemAsync(await admin.GetAsync($"{Base}/orders?pageSize=51", Ct), HttpStatusCode.BadRequest, Ct);
        await ProblemAsync(await admin.GetAsync($"{Base}/orders?page=2147483647", Ct), HttpStatusCode.BadRequest, Ct);
    }

    [Fact]
    public async Task Search_by_order_number_and_email()
    {
        var email = NewEmail();
        var order = await SeedAsync(email, DateTime.UtcNow);
        using var admin = Admin();

        async Task<List<Guid>> SearchAsync(string q)
        {
            var page = await JsonAsync(await admin.GetAsync($"{Base}/orders?q={Uri.EscapeDataString(q)}", Ct), Ct);
            return [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())];
        }

        (await SearchAsync(order.OrderNumber)).ShouldBe([order.Id]);
        (await SearchAsync(order.OrderNumber.ToLowerInvariant())).ShouldBe([order.Id]);
        (await SearchAsync(order.OrderNumber["VL-".Length..])).ShouldBe([order.Id]);
        (await SearchAsync(order.OrderNumber[..^2])).ShouldContain(order.Id);
        (await SearchAsync(email.ToUpperInvariant())).ShouldBe([order.Id]);
        (await SearchAsync(email[..12])).ShouldContain(order.Id);
        (await SearchAsync("%")).ShouldNotContain(order.Id);
        (await SearchAsync("no-such-" + Guid.NewGuid().ToString("N"))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Ship_then_deliver_extends_history()
    {
        var email = NewEmail();
        var order = await SeedAsync(email, DateTime.UtcNow.AddHours(-1), OrderStatus.Paid);
        using var admin = Admin();

        var detail = await JsonAsync(await admin.GetAsync($"{Base}/orders/{order.Id}", Ct), Ct);
        detail.GetProperty("customerEmail").GetString().ShouldBe(email);
        detail.GetProperty("customerId").GetGuid().ShouldBe(order.CustomerId);
        detail.GetProperty("refundRequired").GetBoolean().ShouldBeFalse();
        detail.GetProperty("status").GetString().ShouldBe("Paid");

        var shipped = await JsonAsync(await admin.PostAsync($"{Base}/orders/{order.Id}/ship", null, Ct), Ct);
        shipped.GetProperty("status").GetString().ShouldBe("Shipped");
        shipped.GetProperty("customerEmail").GetString().ShouldBe(email);

        var delivered = await JsonAsync(await admin.PostAsync($"{Base}/orders/{order.Id}/deliver", null, Ct), Ct);
        delivered.GetProperty("status").GetString().ShouldBe("Delivered");
        delivered.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("status").GetString())
            .ShouldBe(["Placed", "Paid", "Shipped", "Delivered"]);

        await ProblemAsync(await admin.PostAsync($"{Base}/orders/{order.Id}/ship", null, Ct), HttpStatusCode.Conflict, Ct);
    }

    [Fact]
    public async Task Ship_a_Placed_order_is_409()
    {
        var order = await SeedAsync(NewEmail(), DateTime.UtcNow);
        using var admin = Admin();
        var problem = await ProblemAsync(await admin.PostAsync($"{Base}/orders/{order.Id}/ship", null, Ct), HttpStatusCode.Conflict, Ct);
        problem.GetProperty("type").GetString().ShouldBe("ordering.invalid_transition");
    }

    [Fact]
    public async Task Deliver_a_Paid_order_is_409()
    {
        var order = await SeedAsync(NewEmail(), DateTime.UtcNow, OrderStatus.Paid);
        using var admin = Admin();
        var problem = await ProblemAsync(await admin.PostAsync($"{Base}/orders/{order.Id}/deliver", null, Ct), HttpStatusCode.Conflict, Ct);
        problem.GetProperty("type").GetString().ShouldBe("ordering.invalid_transition");
    }

    [Fact]
    public async Task Unknown_order_is_404()
    {
        using var admin = Admin();
        var id = Guid.NewGuid();
        foreach (var response in new[]
                 {
                     await admin.GetAsync($"{Base}/orders/{id}", Ct),
                     await admin.PostAsync($"{Base}/orders/{id}/ship", null, Ct),
                     await admin.PostAsync($"{Base}/orders/{id}/deliver", null, Ct),
                 })
        {
            var problem = await ProblemAsync(response, HttpStatusCode.NotFound, Ct);
            problem.GetProperty("type").GetString().ShouldBe("ordering.order_not_found");
        }
    }

    [Fact]
    public async Task Concurrent_ships_yield_one_success_and_one_409()
    {
        var order = await SeedAsync(NewEmail(), DateTime.UtcNow, OrderStatus.Paid);
        using var admin = Admin();
        var responses = await Task.WhenAll(
            admin.PostAsync($"{Base}/orders/{order.Id}/ship", null, Ct),
            admin.PostAsync($"{Base}/orders/{order.Id}/ship", null, Ct));
        responses.Select(r => r.StatusCode).Order().ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict]);
    }
}
