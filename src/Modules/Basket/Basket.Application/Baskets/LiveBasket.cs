using Catalog.Contracts.Variants;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.Application.Baskets;

internal static class LiveBasket
{
    /// <summary>Fetches live snapshots for the basket's lines (at most 20, well under the query's limit) and builds the view.</summary>
    public static async Task<BasketDto> ViewAsync(BasketAggregate? basket, ICatalogVariantQuery catalog, CancellationToken ct)
    {
        if (basket is null || basket.Lines.Count == 0)
        {
            return BasketDto.Empty;
        }

        var snapshots = await catalog.GetAsync([.. basket.Lines.Select(l => l.VariantId)], ct);
        return BasketView.Build(basket, snapshots);
    }
}
