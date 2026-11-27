namespace Payments.Contracts.Payments;

/// <summary>What Ordering needs to report a checkout's payment step.</summary>
public interface IPaymentCheckoutQuery
{
    /// <summary>The order's payment, or null while none has been opened.</summary>
    Task<PaymentCheckoutInfo?> GetByOrderAsync(Guid orderId, CancellationToken ct);
}
