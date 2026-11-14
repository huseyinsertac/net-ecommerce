namespace Catalog.Application.Products;

public sealed record ProductSummaryDto(
    Guid Id, string Slug, string Name, string Brand, string CategorySlug, string ImageUrl,
    decimal PriceFrom, decimal PriceTo, bool InStock, bool IsFeatured);

public sealed record ProductImageDto(string Url, string ThumbUrl);

public sealed record CategoryRefDto(string Slug, string Name);

/// <param name="Value">Display-formatted: "16 GB", "Yes"/"No", "Windows 11".</param>
public sealed record SpecRowDto(string Key, string Label, string Value);

/// <param name="Values">Distinct values in first-seen variant order.</param>
public sealed record OptionAxisDto(string Key, string Label, IReadOnlyList<string> Values);

public sealed record VariantDto(Guid Id, string Sku, IReadOnlyDictionary<string, string> Options, decimal Price, int Available);

public sealed record ProductDetailDto(
    Guid Id, string Slug, string Name, string Brand, string Description,
    IReadOnlyList<CategoryRefDto> Breadcrumb,
    IReadOnlyList<ProductImageDto> Images,
    IReadOnlyList<SpecRowDto> Specs,
    IReadOnlyList<OptionAxisDto> Options,
    IReadOnlyList<VariantDto> Variants);

public static class CatalogImageUrls
{
    public static string Product(string key) => $"/images/products/{key}.webp";

    public static string ProductThumb(string key) => $"/images/products/{key}-400.webp";

    public static string Category(string slug) => $"/images/categories/{slug}.webp";
}
