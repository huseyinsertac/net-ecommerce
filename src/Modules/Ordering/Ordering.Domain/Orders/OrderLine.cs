using Shop.SharedKernel;

namespace Ordering.Domain.Orders;

public sealed class OrderLine : Entity<Guid>
{
    private OrderLine() { }

    public Guid VariantId { get; private set; }
    public string ProductName { get; private set; } = null!;
    public string Sku { get; private set; } = null!;
    public string OptionsLabel { get; private set; } = null!;
    public string? ThumbnailUrl { get; private set; }
    public Money UnitPrice { get; private set; } = null!;
    public int Quantity { get; private set; }
    public Money LineTotal { get; private set; } = null!;

    internal static OrderLine Create(NewOrderLine line)
    {
        var unitPrice = Money.Usd(line.UnitPrice);
        return new OrderLine
        {
            Id = Guid.CreateVersion7(),
            VariantId = line.VariantId,
            ProductName = line.ProductName,
            Sku = line.Sku,
            OptionsLabel = line.OptionsLabel,
            ThumbnailUrl = line.ThumbnailUrl,
            UnitPrice = unitPrice,
            Quantity = line.Quantity,
            LineTotal = unitPrice * line.Quantity,
        };
    }
}
