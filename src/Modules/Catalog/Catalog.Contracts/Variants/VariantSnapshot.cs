namespace Catalog.Contracts.Variants;

/// <param name="OptionsLabel">Option values in the product's axis order joined with " · "; empty when the product has no axes.</param>
/// <param name="Available">Stock on hand minus reserved, never negative.</param>
/// <param name="IsPurchasable">True only when the product is Active.</param>
public sealed record VariantSnapshot(
    Guid VariantId,
    Guid ProductId,
    string ProductSlug,
    string ProductName,
    string Brand,
    string Sku,
    string OptionsLabel,
    decimal UnitPrice,
    int Available,
    string? ThumbnailUrl,
    bool IsPurchasable);
