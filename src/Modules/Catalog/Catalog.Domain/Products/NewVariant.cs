using Shop.SharedKernel;

namespace Catalog.Domain.Products;

public sealed record NewVariant(string Sku, IReadOnlyDictionary<string, string> Options, Money Price, int StockOnHand);
