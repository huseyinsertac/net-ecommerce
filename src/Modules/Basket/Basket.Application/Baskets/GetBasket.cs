using Basket.Application.Abstractions;
using Basket.Domain.Baskets;
using Catalog.Contracts.Variants;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Basket.Application.Baskets;

/// <summary>The owner's basket with live prices; a <c>null</c> owner (no usable cookie) means an empty basket.</summary>
public sealed record GetBasket(BasketOwner? Owner) : IQuery<BasketDto>;

internal sealed class GetBasketHandler(IBasketRepository baskets, ICatalogVariantQuery catalog)
    : IQueryHandler<GetBasket, BasketDto>
{
    public async Task<Result<BasketDto>> Handle(GetBasket query, CancellationToken ct)
    {
        if (query.Owner is null)
        {
            return BasketDto.Empty;
        }

        return await LiveBasket.ViewAsync(await baskets.GetAsync(query.Owner, ct), catalog, ct);
    }
}
