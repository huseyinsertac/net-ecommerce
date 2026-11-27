namespace Ordering.Domain.Orders;

/// <summary>Snapshot of a basket line, as supplied when placing an order.</summary>
public sealed record NewOrderLine(
    Guid VariantId,
    string ProductName,
    string Sku,
    string OptionsLabel,
    string? ThumbnailUrl,
    decimal UnitPrice,
    int Quantity);
