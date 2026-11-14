using Shop.SharedKernel;

namespace Catalog.Domain.Products;

public sealed class ProductVariant : Entity<Guid>
{
    private Dictionary<string, string> _options = [];

    private ProductVariant() { }

    public string Sku { get; private set; } = string.Empty;
    public IReadOnlyDictionary<string, string> Options => _options;
    public Money Price { get; private set; } = Money.Zero;
    public int StockOnHand { get; private set; }
    public int StockReserved { get; private set; }
    public int Available => StockOnHand - StockReserved;

    internal static ProductVariant Create(string sku, Dictionary<string, string> options, Money price, int stockOnHand) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Sku = sku,
            _options = options,
            Price = price,
            StockOnHand = stockOnHand,
        };

    internal void Reserve(int quantity) => StockReserved += quantity;

    internal void Release(int quantity) => StockReserved -= quantity;

    internal void Commit(int quantity)
    {
        StockOnHand -= quantity;
        StockReserved -= quantity;
    }
}
