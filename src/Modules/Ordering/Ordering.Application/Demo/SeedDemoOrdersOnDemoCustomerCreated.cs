using System.Security.Cryptography;
using System.Text;
using Catalog.Contracts.Variants;
using Identity.Contracts.Events;
using Microsoft.Extensions.Logging;
using Ordering.Application.Abstractions;
using Ordering.Application.Checkout;
using Ordering.Domain.Orders;
using Shop.Application.Abstractions.Events;

namespace Ordering.Application.Demo;

/// <summary>
/// Gives every new demo customer three past orders (two Delivered, one Shipped) so the order history is not empty. The
/// orders are historical: no OrderPlaced is published and no stock is touched. A thin catalog gets fewer orders (or
/// none), never an error, because a demo sign-in must not depend on this. The inbox wrapper makes a redelivery a no-op;
/// the deterministic idempotency keys protect a replay that bypasses it.
/// </summary>
internal sealed class SeedDemoOrdersOnDemoCustomerCreated(
    IOrderRepository orders,
    ICatalogVariantQuery catalog,
    TimeProvider clock,
    ILogger<SeedDemoOrdersOnDemoCustomerCreated> logger) : IIntegrationEventHandler<DemoCustomerCreated>
{
    private const int SampleSize = 4;

    private sealed record Plan(char Letter, int DaysAgo, bool Delivered, (int Sample, int Quantity)[] Lines);

    private static readonly Plan[] Plans =
    [
        new('A', 41, true, [(0, 1)]),
        new('B', 12, true, [(1, 1), (2, 2)]),
        new('C', 2, false, [(3, 1)]),
    ];

    public async Task Handle(DemoCustomerCreated e, CancellationToken ct)
    {
        var address = ShippingAddress.Create(
            "Demo Customer", "221 Market Street", null, "San Francisco", "94105", "US").Value;
        var samples = await catalog.GetSampleAsync(SampleSize, ct);
        var now = clock.GetUtcNow().UtcDateTime;

        foreach (var plan in Plans)
        {
            var lines = plan.Lines
                .Where(l => l.Sample < samples.Count)
                .Select(l => ToLine(samples[l.Sample], l.Quantity))
                .ToList();
            if (lines.Count == 0)
            {
                continue;
            }

            var key = KeyFor(e.UserId, plan.Letter);
            if (await orders.GetByIdempotencyKeyAsync(e.UserId, key, ct) is not null)
            {
                continue;
            }

            var placedAt = now - TimeSpan.FromDays(plan.DaysAgo);
            var number = await orders.NextOrderNumberAsync(ct);
            var placed = Order.PlaceHistorical(
                $"{PlaceOrderHandler.OrderNumberPrefix}{number}", e.UserId, e.Email, key, address, lines, placedAt);
            if (placed.IsFailure)
            {
                logger.LogWarning("Demo order {Letter} for {UserId} not seeded ({Code}).", plan.Letter, e.UserId, placed.Error.Code);
                continue;
            }

            var order = placed.Value;
            var paid = order.MarkPaid(placedAt.AddHours(1));
            var shipped = paid.IsSuccess ? order.Ship(placedAt.AddDays(1)) : paid;
            var done = shipped.IsSuccess && plan.Delivered ? order.Deliver(placedAt.AddDays(4)) : shipped;
            if (done.IsFailure)
            {
                logger.LogWarning("Demo order {Letter} for {UserId} not advanced ({Code}).", plan.Letter, e.UserId, done.Error.Code);
                continue;
            }

            orders.Add(order);
        }
    }

    private static NewOrderLine ToLine(VariantSnapshot s, int quantity) =>
        new(s.VariantId, s.ProductName, s.Sku, s.OptionsLabel, s.ThumbnailUrl, s.UnitPrice, quantity);

    /// <summary>SHA-1 of "{userId}:{letter}", first 16 bytes: the same user and letter always give the same key.</summary>
    internal static Guid KeyFor(Guid userId, char letter) =>
        new(SHA1.HashData(Encoding.UTF8.GetBytes($"{userId}:{letter}"))[..16]);
}
