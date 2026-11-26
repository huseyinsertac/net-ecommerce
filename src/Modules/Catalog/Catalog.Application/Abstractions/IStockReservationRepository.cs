using Catalog.Domain.Products;
using Catalog.Domain.Reservations;

namespace Catalog.Application.Abstractions;

/// <summary>Loads the products (with variants) and stock reservations the reservation handlers work on.</summary>
public interface IStockReservationRepository
{
    Task<StockReservation?> GetAsync(Guid orderId, CancellationToken ct);

    /// <summary>Every product owning at least one of the variants, tracked, with all its variants.</summary>
    Task<IReadOnlyList<Product>> GetProductsByVariantIdsAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken ct);

    void Add(StockReservation reservation);

    /// <summary>Forgets all tracked changes, so a half-applied reservation is never saved.</summary>
    void DiscardChanges();
}
