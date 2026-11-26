using Catalog.Contracts.Variants;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.Application.Baskets;

/// <summary>
/// Joins a stored basket with live catalog snapshots. Pure: prices and stock are never stored in the basket, so a line whose
/// variant vanished, went inactive, ran out or changed price is flagged here rather than failing the read.
/// </summary>
public static class BasketView
{
    public static BasketDto Build(BasketAggregate? basket, IReadOnlyDictionary<Guid, VariantSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (basket is null || basket.Lines.Count == 0)
        {
            return BasketDto.Empty;
        }

        var lines = basket.Lines
            .OrderBy(l => l.AddedAtUtc)
            .ThenBy(l => l.Id)
            .Select(l => Line(l.VariantId, l.Quantity, snapshots.GetValueOrDefault(l.VariantId)))
            .ToList();

        return new BasketDto(
            lines,
            lines.Sum(l => l.Quantity),
            lines.Where(l => l.Issue is null).Sum(l => l.LineTotal ?? 0m),
            lines.Any(l => l.Issue is not null));
    }

    private static BasketLineDto Line(Guid variantId, int quantity, VariantSnapshot? s)
    {
        if (s is null)
        {
            return new BasketLineDto(variantId, null, null, null, null, null, null, null, quantity, 0, null,
                BasketLineIssue.Unavailable);
        }

        BasketLineIssue? issue = !s.IsPurchasable ? BasketLineIssue.Unavailable
            : s.Available < quantity ? BasketLineIssue.InsufficientStock
            : null;

        return new BasketLineDto(
            variantId, s.ProductSlug, s.ProductName, s.Brand, s.Sku, s.OptionsLabel, s.ThumbnailUrl,
            s.UnitPrice, quantity,
            Available: s.IsPurchasable ? s.Available : 0,
            LineTotal: issue is null ? s.UnitPrice * quantity : null,
            issue);
    }
}
