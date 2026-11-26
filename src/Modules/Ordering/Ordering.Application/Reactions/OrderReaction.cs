using Microsoft.Extensions.Logging;
using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;
using Shop.SharedKernel;

namespace Ordering.Application.Reactions;

/// <summary>
/// Applies an event's transition to a still-Placed order. Redeliveries are stopped earlier by the inbox; a late or
/// out-of-order event (the order is unknown or has already moved on) is logged at Warning and skipped, never thrown, so
/// the outbox does not retry something that can never succeed. The inbox wrapper performs the save.
/// </summary>
internal static class OrderReaction
{
    /// <param name="logNotPlaced">
    /// Logs the order having moved on in a handler-specific way and returns true; returning false falls back to the Warning.
    /// </param>
    public static async Task ApplyToPlacedAsync(
        IOrderRepository orders,
        Guid orderId,
        string eventName,
        Func<Order, Result> transition,
        ILogger logger,
        CancellationToken ct,
        Func<Order, bool>? logNotPlaced = null)
    {
        var order = await orders.GetAsync(orderId, ct);
        if (order is null)
        {
            logger.LogWarning("{Event} for unknown order {OrderId}; ignoring.", eventName, orderId);
            return;
        }

        // The domain also allows cancelling a Paid order (an admin action), but a stock or payment event must never undo
        // a payment, so reactions only ever move a Placed order.
        if (order.Status != OrderStatus.Placed)
        {
            if (logNotPlaced?.Invoke(order) != true)
            {
                logger.LogWarning(
                    "{Event} for order {OrderId}, which is already {Status}; ignoring.", eventName, orderId, order.Status);
            }

            return;
        }

        var result = transition(order);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "{Event} for order {OrderId} not applied ({ErrorCode}); ignoring.", eventName, orderId, result.Error.Code);
        }
    }
}
