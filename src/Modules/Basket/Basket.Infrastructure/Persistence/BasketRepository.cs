using Basket.Application.Abstractions;
using Basket.Domain.Baskets;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.Infrastructure.Persistence;

internal sealed class BasketRepository(BasketDbContext db) : IBasketRepository
{
    /// <summary>Unique indexes a concurrent request can trip: two first writes for one owner, or the same new line twice.</summary>
    private static readonly HashSet<string> RaceIndexes =
        [BasketConfiguration.UserIndex, BasketConfiguration.AnonymousIndex, BasketLineConfiguration.VariantIndex];

    private IQueryable<BasketAggregate> Baskets => db.Baskets.Include(b => b.Lines);

    public Task<BasketAggregate?> GetAsync(BasketOwner owner, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return owner.UserId is { } userId
            ? Baskets.SingleOrDefaultAsync(b => b.Owner.UserId == userId, ct)
            : Baskets.SingleOrDefaultAsync(b => b.Owner.AnonymousId == owner.AnonymousId, ct);
    }

    public void Add(BasketAggregate basket) => db.Baskets.Add(basket);

    public void Remove(BasketAggregate basket) => db.Baskets.Remove(basket);

    /// <summary>Saves, translating EF/Npgsql races into the exception the application layer understands.</summary>
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Forget the stale entities so a reload in this scope reads the current rows.
            db.ChangeTracker.Clear();
            throw new BasketConcurrencyException(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                           && pg.ConstraintName is { } name && RaceIndexes.Contains(name))
        {
            db.ChangeTracker.Clear();
            throw new BasketConcurrencyException(ex);
        }
    }
}
