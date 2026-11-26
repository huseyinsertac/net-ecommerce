using Basket.Contracts;
using Basket.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basket.Infrastructure.Reading;

internal sealed class BasketCheckoutQuery(BasketDbContext db) : IBasketCheckoutQuery
{
    public async Task<IReadOnlyList<BasketItem>> GetForUserAsync(Guid userId, CancellationToken ct)
    {
        var basket = await db.Baskets
            .AsNoTracking()
            .Include(b => b.Lines)
            .SingleOrDefaultAsync(b => b.Owner.UserId == userId, ct);

        return basket is null
            ? []
            : [.. basket.Lines.OrderBy(l => l.AddedAtUtc).ThenBy(l => l.Id).Select(l => new BasketItem(l.VariantId, l.Quantity))];
    }
}
