using Shop.SharedKernel;

namespace Catalog.Contracts.Events;

/// <summary>Stock for every line of the order is now reserved.</summary>
public sealed record StockReserved(Guid OrderId) : IntegrationEvent;

/// <summary>Nothing was reserved; <paramref name="Skus"/> are the SKUs that were short or unavailable.</summary>
public sealed record StockReservationFailed(Guid OrderId, IReadOnlyList<string> Skus) : IntegrationEvent;
