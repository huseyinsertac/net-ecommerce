using Catalog.Contracts.Events;
using Microsoft.Extensions.Logging;
using Ordering.Contracts.Orders;
using Payments.Application.Abstractions;
using Payments.Application.Gateways;
using Payments.Domain.Payments;
using Shop.Application.Abstractions.Events;
using Shop.SharedKernel;

namespace Payments.Application.Payments;

/// <summary>
/// Opens the payment session once the order's stock is held. At most one payment per order: a redelivery or a second
/// StockReserved finds the existing payment and stops. The gateway call happens before the save, so a failed save is
/// retried by the outbox; the gateway is idempotent per order (Stripe: idempotency key <c>checkout-session-{orderId}</c>),
/// so the retry gets the same session rather than a second one. The inbox wrapper performs the save.
/// </summary>
internal sealed class StartPaymentOnStockReserved(
    IPaymentRepository payments,
    IOrderPaymentQuery orders,
    IPaymentGateway gateway,
    PaymentSessionSettings settings,
    TimeProvider clock,
    ILogger<StartPaymentOnStockReserved> logger) : IIntegrationEventHandler<StockReserved>
{
    public async Task Handle(StockReserved e, CancellationToken ct)
    {
        if (await payments.ExistsForOrderAsync(e.OrderId, ct))
        {
            logger.LogInformation("Order {OrderId} already has a payment; ignoring StockReserved.", e.OrderId);
            return;
        }

        // Ordering commits the order before publishing OrderPlaced, so a missing order is a bug; failing lets the outbox
        // retry (and eventually park) the message instead of silently dropping the checkout.
        var order = await orders.GetAsync(e.OrderId, ct)
            ?? throw new InvalidOperationException($"Stock was reserved for order {e.OrderId}, which does not exist.");

        var now = clock.GetUtcNow().UtcDateTime;
        var expiresAt = now + settings.Timeout;
        var orderPage = $"{settings.PublicBaseUrl}/orders/{order.OrderId}";
        var session = await gateway.CreateSessionAsync(
            new GatewaySessionRequest(
                order.OrderId,
                order.OrderNumber,
                order.CustomerEmail,
                order.Total,
                order.Currency,
                order.Lines,
                SuccessUrl: $"{orderPage}?checkout=success",
                CancelUrl: $"{orderPage}?checkout=cancelled",
                expiresAt),
            ct);

        payments.Add(Payment.Start(
            order.OrderId, order.CustomerId, new Money(order.Total, order.Currency), gateway.Name,
            session.SessionId, session.Url, now, expiresAt));
        logger.LogInformation(
            "Opened {Provider} payment session for order {OrderNumber} ({OrderId}).", gateway.Name, order.OrderNumber, order.OrderId);
    }
}
