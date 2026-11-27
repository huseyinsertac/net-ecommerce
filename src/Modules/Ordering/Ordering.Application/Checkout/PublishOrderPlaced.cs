using Ordering.Contracts.Events;
using Ordering.Domain.Orders;
using Shop.Application.Abstractions.Events;

namespace Ordering.Application.Checkout;

/// <summary>
/// Turns the domain event into the integration event Catalog (and later Payments) react to. Runs inside the order's
/// SaveChanges, so the outbox row commits in the same transaction as the order.
/// </summary>
internal sealed class PublishOrderPlaced(IEventPublisher publisher) : IDomainEventHandler<OrderPlacedDomainEvent>
{
    public Task Handle(OrderPlacedDomainEvent e, CancellationToken ct)
    {
        var order = e.Order;
        publisher.Publish(new OrderPlaced(
            order.Id,
            order.CustomerId,
            [.. order.Lines.OrderBy(l => l.LineNumber).Select(l => new OrderPlacedLine(l.VariantId, l.Quantity))],
            order.Total.Amount,
            order.Total.Currency)
        {
            // Basket uses this to tell lines that were ordered from lines added afterwards.
            OccurredOnUtc = order.PlacedAtUtc,
        });
        return Task.CompletedTask;
    }
}
