using Basket.Application.Abstractions;
using Basket.Domain.Baskets;
using Catalog.Contracts.Variants;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.Application.Baskets;

/// <summary>
/// Moves the anonymous basket's lines into the user's (quantities summed and capped by the aggregate) and deletes the
/// anonymous basket, in one save. Without an anonymous id or basket it returns the user's basket unchanged, so a repeat
/// is a no-op.
/// </summary>
public sealed record MergeBaskets(Guid UserId, Guid? AnonymousId) : ICommand<BasketDto>;

internal sealed class MergeBasketsHandler(IBasketRepository baskets, ICatalogVariantQuery catalog, TimeProvider clock)
    : ICommandHandler<MergeBaskets, BasketDto>
{
    public async Task<Result<BasketDto>> Handle(MergeBaskets command, CancellationToken ct)
    {
        var userOwner = BasketOwner.ForUser(command.UserId);
        var userBasket = await baskets.GetAsync(userOwner, ct);

        var anonymous = command.AnonymousId is { } anonymousId && anonymousId != Guid.Empty
            ? await baskets.GetAsync(BasketOwner.ForAnonymous(anonymousId), ct)
            : null;
        if (anonymous is null)
        {
            return await LiveBasket.ViewAsync(userBasket, catalog, ct);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (userBasket is null)
        {
            userBasket = BasketAggregate.Create(userOwner, now);
            baskets.Add(userBasket);
        }

        userBasket.MergeFrom(anonymous, now);
        baskets.Remove(anonymous);
        try
        {
            await baskets.SaveChangesAsync(ct);
        }
        catch (BasketConcurrencyException)
        {
            return BasketErrors.ConcurrentUpdate();
        }

        return await LiveBasket.ViewAsync(userBasket, catalog, ct);
    }
}
