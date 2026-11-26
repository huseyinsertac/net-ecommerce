using Basket.Domain.Baskets;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.Application.Abstractions;

/// <summary>Loads and saves the <see cref="BasketAggregate"/>, always with its lines.</summary>
public interface IBasketRepository
{
    Task<BasketAggregate?> GetAsync(BasketOwner owner, CancellationToken ct);

    void Add(BasketAggregate basket);

    void Remove(BasketAggregate basket);

    /// <summary>Saves all changes in one transaction.</summary>
    /// <exception cref="BasketConcurrencyException">Another request changed or created the same basket first.</exception>
    Task SaveChangesAsync(CancellationToken ct);
}
