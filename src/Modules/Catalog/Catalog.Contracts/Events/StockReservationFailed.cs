using Shop.SharedKernel;

namespace Catalog.Contracts.Events;

/// <summary>Nothing was reserved; <paramref name="Skus"/> are the SKUs that were short or unavailable.</summary>
public sealed record StockReservationFailed(Guid OrderId, IReadOnlyList<string> Skus) : IntegrationEvent;
