using Microsoft.Extensions.Logging;
using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;
using Payments.Contracts.Events;
using Shop.Application.Abstractions.Events;

namespace Ordering.Application.Reactions;

/// <summary>Cancels the order when its payment was declined or expired. Catalog releases the stock on the same events.</summary>
internal sealed class CancelOnPaymentEnded(IOrderRepository orders, TimeProvider clock, ILogger<CancelOnPaymentEnded> logger)
    : IIntegrationEventHandler<PaymentFailed>, IIntegrationEventHandler<PaymentExpired>
{
    public Task Handle(PaymentFailed e, CancellationToken ct) =>
        Cancel(e.OrderId, nameof(PaymentFailed), CancellationReason.PaymentFailed, ct);

    public Task Handle(PaymentExpired e, CancellationToken ct) =>
        Cancel(e.OrderId, nameof(PaymentExpired), CancellationReason.PaymentExpired, ct);

    private Task Cancel(Guid orderId, string eventName, CancellationReason reason, CancellationToken ct) =>
        OrderReaction.ApplyToPlacedAsync(
            orders, orderId, eventName, order => order.Cancel(reason, clock.GetUtcNow().UtcDateTime), logger, ct);
}
