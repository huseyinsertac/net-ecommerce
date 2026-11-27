using Ordering.Contracts.Orders;

namespace Payments.Application.Gateways;

/// <param name="Total">The order total in major units (dollars); gateways convert as they need.</param>
public sealed record GatewaySessionRequest(
    Guid OrderId,
    string OrderNumber,
    string CustomerEmail,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderPaymentLine> Lines,
    string SuccessUrl,
    string CancelUrl,
    DateTime ExpiresAtUtc);
