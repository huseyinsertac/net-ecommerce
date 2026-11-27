using System.Net;
using Catalog.Contracts.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Persistence;
using Payments.Contracts.Events;
using Shop.Infrastructure.Common.Events;
using Shop.SharedKernel;
using Shouldly;
using static Shop.IntegrationTests.Ordering.OrderingTestClient;

namespace Shop.IntegrationTests.Ordering;

/// <summary>Ordering's reactions to late or out-of-order events: skipped, never thrown, the order left as it was.</summary>
[Collection(ShopApiCollection.Name)]
public sealed class OrderReactionTests(ShopApiFactory factory)
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

    /// <summary>Saves a placed order (optionally moved on by <paramref name="moveTo"/>) straight through the context, without its domain events.</summary>
    private async Task<Order> SeedOrderAsync(Func<Order, Result>? moveTo = null)
    {
        var address = ShippingAddress.Create("Ada Lovelace", "12 Analytical Row", null, "London", "N1 9GU", "GB").Value;
        var order = Order.Place(
            $"TEST-{Guid.NewGuid():N}"[..20], Guid.NewGuid(), "seed@test.voltlane", Guid.NewGuid(), address,
            [new NewOrderLine(Guid.NewGuid(), "Laptop", "SKU-1", "Silver", null, 10m, 1)], DateTime.UtcNow).Value;
        if (moveTo is not null)
        {
            moveTo(order).IsSuccess.ShouldBeTrue();
        }

        order.ClearDomainEvents();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);
        return order;
    }

    private async Task<Order> ReloadAsync(Guid orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        return await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId, Ct);
    }

    private Task DeliverAsync(IIntegrationEvent e) => Services.GetRequiredService<IEventBus>().PublishAsync(e, Ct);

    [Fact]
    public async Task Payment_succeeded_for_a_cancelled_order_is_skipped_and_the_order_stays_cancelled()
    {
        var order = await SeedOrderAsync(o => o.Cancel(CancellationReason.PaymentExpired, DateTime.UtcNow));

        await DeliverAsync(new PaymentSucceeded(order.Id, Guid.NewGuid()));

        var reloaded = await ReloadAsync(order.Id);
        reloaded.Status.ShouldBe(OrderStatus.Cancelled);
        reloaded.CancellationReason.ShouldBe(CancellationReason.PaymentExpired);
        reloaded.History.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Late_cancelling_events_never_undo_a_payment()
    {
        var order = await SeedOrderAsync(o => o.MarkPaid(DateTime.UtcNow));

        await DeliverAsync(new StockReservationFailed(order.Id, ["SKU-1"]));
        await DeliverAsync(new PaymentFailed(order.Id, Guid.NewGuid(), "declined"));
        await DeliverAsync(new PaymentExpired(order.Id, Guid.NewGuid()));

        var reloaded = await ReloadAsync(order.Id);
        reloaded.Status.ShouldBe(OrderStatus.Paid);
        reloaded.RefundRequired.ShouldBeFalse();
    }

    [Fact]
    public async Task Replayed_event_is_applied_once()
    {
        var order = await SeedOrderAsync();
        var e = new PaymentSucceeded(order.Id, Guid.NewGuid());

        await DeliverAsync(e);
        await DeliverAsync(e);

        var reloaded = await ReloadAsync(order.Id);
        reloaded.Status.ShouldBe(OrderStatus.Paid);
        reloaded.History.Select(h => h.Status).ShouldBe([OrderStatus.Placed, OrderStatus.Paid]);
    }

    [Fact]
    public async Task Events_for_unknown_orders_are_skipped()
    {
        await Should.NotThrowAsync(() => DeliverAsync(new StockReservationFailed(Guid.NewGuid(), ["SKU-X"])));
        await Should.NotThrowAsync(() => DeliverAsync(new PaymentSucceeded(Guid.NewGuid(), Guid.NewGuid())));
    }

    [Fact]
    public async Task Checkout_status_of_someone_elses_order_is_404_and_needs_sign_in()
    {
        var laptop = await SeedLaptopAsync(Services, Ct);
        using var owner = await SignInAsync(factory, Ct);
        using var stranger = await SignInAsync(factory, Ct);
        await AddToBasketAsync(owner.Client, Silver(laptop), 1, Ct);
        var (orderId, _) = await PlaceAsync(owner.Client, 1099.99m, Ct);

        var response = await stranger.Client.GetAsync($"/api/ordering/orders/{orderId}/checkout-status", Ct);
        (await ProblemAsync(response, HttpStatusCode.NotFound, Ct)).GetProperty("type").GetString()
            .ShouldBe("ordering.order_not_found");

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/ordering/orders/{orderId}/checkout-status", Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }
}
