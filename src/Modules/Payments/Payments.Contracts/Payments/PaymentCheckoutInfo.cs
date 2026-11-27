namespace Payments.Contracts.Payments;

/// <param name="Status">The payment status name: <c>Pending</c>, <c>Succeeded</c>, <c>Failed</c> or <c>Expired</c>.</param>
/// <param name="CheckoutUrl">Where the shopper pays; only while the payment is Pending, otherwise null.</param>
public sealed record PaymentCheckoutInfo(Guid PaymentId, string Status, string? CheckoutUrl, DateTime ExpiresAtUtc);
