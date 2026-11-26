namespace Basket.Domain.Baskets;

/// <summary>Who a basket belongs to: exactly one of <see cref="UserId"/> or <see cref="AnonymousId"/> is set.</summary>
public sealed record BasketOwner
{
    private BasketOwner(Guid? userId, Guid? anonymousId)
    {
        UserId = userId;
        AnonymousId = anonymousId;
    }

    public Guid? UserId { get; }
    public Guid? AnonymousId { get; }

    public static BasketOwner ForUser(Guid id) =>
        id == Guid.Empty ? throw new ArgumentException("User id must not be empty.", nameof(id)) : new(id, null);

    public static BasketOwner ForAnonymous(Guid id) =>
        id == Guid.Empty ? throw new ArgumentException("Anonymous id must not be empty.", nameof(id)) : new(null, id);
}
