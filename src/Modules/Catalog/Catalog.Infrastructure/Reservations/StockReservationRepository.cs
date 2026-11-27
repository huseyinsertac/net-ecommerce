using Catalog.Application.Abstractions;
using Catalog.Domain.Products;
using Catalog.Domain.Reservations;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure.Reservations;

internal sealed class StockReservationRepository(CatalogDbContext db) : IStockReservationRepository
{
    public Task<StockReservation?> GetAsync(Guid orderId, CancellationToken ct) =>
        db.StockReservations.SingleOrDefaultAsync(r => r.Id == orderId, ct);

    public async Task<IReadOnlyList<Product>> GetProductsByVariantIdsAsync(
        IReadOnlyCollection<Guid> variantIds, CancellationToken ct)
    {
        var ids = variantIds.ToArray();
        return await db.Products
            .Include(p => p.Variants)
            .Where(p => p.Variants.Any(v => ids.Contains(v.Id)))
            .ToListAsync(ct);
    }

    public void Add(StockReservation reservation) => db.StockReservations.Add(reservation);

    public void DiscardChanges() => db.ChangeTracker.Clear();
}
