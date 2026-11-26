using Basket.Application.Abstractions;
using Basket.Domain.Baskets;
using Catalog.Contracts.Variants;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Basket.Application.Baskets;

/// <summary>Removes a line. Removing something that is not there, or from no basket, succeeds.</summary>
public sealed record RemoveItem(BasketOwner? Owner, Guid VariantId) : ICommand<BasketDto>;

internal sealed class RemoveItemHandler(IBasketRepository baskets, ICatalogVariantQuery catalog, TimeProvider clock)
    : ICommandHandler<RemoveItem, BasketDto>
{
    public async Task<Result<BasketDto>> Handle(RemoveItem command, CancellationToken ct)
    {
        var basket = command.Owner is null ? null : await baskets.GetAsync(command.Owner, ct);
        if (basket is null)
        {
            return BasketDto.Empty;
        }

        if (basket.Lines.Any(l => l.VariantId == command.VariantId))
        {
            basket.Remove(command.VariantId, clock.GetUtcNow().UtcDateTime);
            try
            {
                await baskets.SaveChangesAsync(ct);
            }
            catch (BasketConcurrencyException)
            {
                return BasketErrors.ConcurrentUpdate();
            }
        }

        return await LiveBasket.ViewAsync(basket, catalog, ct);
    }
}
