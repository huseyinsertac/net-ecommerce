namespace Ordering.Contracts.Orders;

/// <summary>What Payments needs to open a checkout session for an order.</summary>
public interface IOrderPaymentQuery
{
    /// <summary>The order's payment details, or null when there is no such order.</summary>
    Task<OrderPaymentDetails?> GetAsync(Guid orderId, CancellationToken ct);
}
