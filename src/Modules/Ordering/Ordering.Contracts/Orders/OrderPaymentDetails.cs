namespace Ordering.Contracts.Orders;

public sealed record OrderPaymentDetails(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    string CustomerEmail,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderPaymentLine> Lines);

/// <param name="Name">"ProductName (OptionsLabel)", or just the product name when the variant has no options.</param>
public sealed record OrderPaymentLine(string Name, int Quantity, decimal UnitPrice);
