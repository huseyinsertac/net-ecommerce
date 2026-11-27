using Microsoft.Extensions.Logging;
using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;
using Payments.Contracts.Events;
using Shop.Application.Abstractions.Events;

namespace Ordering.Application.Reactions;

/// <summary>
/// Marks the order Paid. Money arriving for an order that is already Cancelled is logged at Error: the customer needs a
/// refund, which is not automated yet.
/// </summary>
internal sealed class MarkPaidOnPaymentSucceeded(
    IOrderRepository orders, TimeProvider clock, ILogger<MarkPaidOnPaymentSucceeded> logger)
    : IIntegrationEventHandler<PaymentSucceeded>
{
    public Task Handle(PaymentSucceeded e, CancellationToken ct) =>
        OrderReaction.ApplyToPlacedAsync(
            orders, e.OrderId, nameof(PaymentSucceeded),
            order => order.MarkPaid(clock.GetUtcNow().UtcDateTime), logger, ct,
            logNotPlaced: order =>
            {
                if (order.Status != OrderStatus.Cancelled)
                {
                    return false;
                }

                logger.LogError(
                    "Payment {PaymentId} succeeded for order {OrderId}, which is Cancelled ({Reason}); the customer must be refunded.",
                    e.PaymentId, e.OrderId, order.CancellationReason);
                return true;
            });
}
