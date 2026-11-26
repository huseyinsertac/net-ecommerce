using Basket.Application.Abstractions;
using Basket.Domain.Baskets;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Basket.Application.Baskets;

/// <summary>Deletes the owner's basket (its lines cascade). No basket is already the desired state.</summary>
public sealed record ClearBasket(BasketOwner? Owner) : ICommand;

internal sealed class ClearBasketHandler(IBasketRepository baskets) : ICommandHandler<ClearBasket>
{
    public async Task<Result> Handle(ClearBasket command, CancellationToken ct)
    {
        var basket = command.Owner is null ? null : await baskets.GetAsync(command.Owner, ct);
        if (basket is null)
        {
            return Result.Success();
        }

        baskets.Remove(basket);
        try
        {
            await baskets.SaveChangesAsync(ct);
        }
        catch (BasketConcurrencyException)
        {
            return Result.Failure(BasketErrors.ConcurrentUpdate());
        }

        return Result.Success();
    }
}
