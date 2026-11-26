using Shop.SharedKernel;

namespace Ordering.Contracts.Events;

/// <summary>An order was placed; Catalog reserves stock for its lines.</summary>
public sealed record OrderPlaced(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<OrderPlacedLine> Lines,
    decimal Total,
    string Currency) : IntegrationEvent;

public sealed record OrderPlacedLine(Guid VariantId, int Quantity);
