using Basket.Application.Abstractions;
using Basket.Domain.Baskets;
using Catalog.Contracts.Variants;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.Application.Baskets;

/// <summary>Sets a line's quantity (0 removes it), creating the basket on the first write.</summary>
public sealed record SetItemQuantity(BasketOwner Owner, Guid VariantId, int Quantity) : ICommand<BasketDto>;

internal sealed class SetItemQuantityHandler(IBasketRepository baskets, ICatalogVariantQuery catalog, TimeProvider clock)
    : ICommandHandler<SetItemQuantity, BasketDto>
{
    public async Task<Result<BasketDto>> Handle(SetItemQuantity command, CancellationToken ct)
    {
        if (command.Quantity is < 0 or > BasketAggregate.MaxQuantityPerLine)
        {
            return BasketErrors.InvalidQuantity(BasketAggregate.MaxQuantityPerLine);
        }

        if (command.Quantity > 0)
        {
            var snapshots = await catalog.GetAsync([command.VariantId], ct);
            if (!snapshots.TryGetValue(command.VariantId, out var variant) || !variant.IsPurchasable)
            {
                return BasketErrors.VariantUnavailable();
            }

            if (command.Quantity > variant.Available)
            {
                return BasketErrors.InsufficientStock(variant.Available);
            }
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var basket = await baskets.GetAsync(command.Owner, ct);
        if (basket is null)
        {
            if (command.Quantity == 0)
            {
                return BasketDto.Empty; // nothing to remove, and no reason to create a row
            }

            basket = BasketAggregate.Create(command.Owner, now);
            baskets.Add(basket);
        }

        var result = basket.SetQuantity(command.VariantId, command.Quantity, now);
        if (result.IsFailure)
        {
            return result.Error;
        }

        try
        {
            await baskets.SaveChangesAsync(ct);
        }
        catch (BasketConcurrencyException)
        {
            return BasketErrors.ConcurrentUpdate();
        }

        return await LiveBasket.ViewAsync(basket, catalog, ct);
    }
}
