using Shop.SharedKernel;

namespace Basket.Domain.Baskets;

public sealed class BasketLine : Entity<Guid>
{
    private BasketLine() { }

    internal BasketLine(Guid variantId, int quantity, DateTime addedAtUtc)
    {
        Id = Guid.CreateVersion7();
        VariantId = variantId;
        Quantity = quantity;
        AddedAtUtc = addedAtUtc;
    }

    public Guid VariantId { get; private set; }
    public int Quantity { get; internal set; }
    public DateTime AddedAtUtc { get; private set; }
}
