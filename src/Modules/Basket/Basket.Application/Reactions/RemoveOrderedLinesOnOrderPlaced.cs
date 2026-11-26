using Basket.Application.Abstractions;
using Basket.Domain.Baskets;
using Ordering.Contracts.Events;
using Shop.Application.Abstractions.Events;

namespace Basket.Application.Reactions;

/// <summary>
/// Once an order is placed, removes the ordered variants from the customer's basket. A line added after the order was
/// placed (<see cref="Shop.SharedKernel.IIntegrationEvent.OccurredOnUtc"/>) is left alone even for an ordered variant, and
/// lines for other variants are never touched. A concurrent basket write makes the save fail, so the outbox retries.
/// The inbox wrapper performs the single save.
/// </summary>
internal sealed class RemoveOrderedLinesOnOrderPlaced(IBasketRepository baskets, TimeProvider clock)
    : IIntegrationEventHandler<OrderPlaced>
{
    public async Task Handle(OrderPlaced e, CancellationToken ct)
    {
        if (e.CustomerId == Guid.Empty)
        {
            return;
        }

        var basket = await baskets.GetAsync(BasketOwner.ForUser(e.CustomerId), ct);
        if (basket is null)
        {
            return;
        }

        var ordered = e.Lines.Select(l => l.VariantId).ToHashSet();
        var toRemove = basket.Lines
            .Where(l => ordered.Contains(l.VariantId) && l.AddedAtUtc <= e.OccurredOnUtc)
            .Select(l => l.VariantId)
            .ToList();

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var variantId in toRemove)
        {
            basket.Remove(variantId, now);
        }
    }
}
