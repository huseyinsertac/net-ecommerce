namespace Basket.Contracts;

/// <summary>Read access to a signed-in user's basket for checkout. Basket owns the lines; prices come from Catalog.</summary>
public interface IBasketCheckoutQuery
{
    /// <summary>The user's basket lines, oldest first; empty when the user has no basket.</summary>
    Task<IReadOnlyList<BasketItem>> GetForUserAsync(Guid userId, CancellationToken ct);
}
