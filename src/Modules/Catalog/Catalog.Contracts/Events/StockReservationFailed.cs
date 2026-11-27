using Shop.SharedKernel;

namespace Catalog.Contracts.Events;

/// <summary>Nothing was reserved; <paramref name="Skus"/> are the SKUs that were short or unavailable.</summary>
/// <param name="Skus">
/// One entry per failed line: its SKU, or, for a variant the catalog does not know (so it has no SKU), the variant id as
/// a string. Meant for logs and support, not for matching against SKUs.
/// </param>
public sealed record StockReservationFailed(Guid OrderId, IReadOnlyList<string> Skus) : IntegrationEvent;
