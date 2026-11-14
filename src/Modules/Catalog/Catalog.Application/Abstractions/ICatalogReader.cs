using Catalog.Application.Categories;
using Catalog.Application.Products;
using Catalog.Domain.Categories;
using Shop.Application.Abstractions.Paging;

namespace Catalog.Application.Abstractions;

/// <summary>The catalog's SQL read side. Every read only ever sees Active products.</summary>
public interface ICatalogReader
{
    Task<PagedResult<ProductSummaryDto>> SearchAsync(ProductSearchCriteria criteria, CancellationToken ct);

    Task<ProductDetailDto?> GetProductAsync(string slug, CancellationToken ct);

    Task<IReadOnlyList<CategoryNodeDto>> GetCategoryTreeAsync(CancellationToken ct);

    Task<CategoryFacetsDto?> GetFacetsAsync(string slug, CancellationToken ct);

    Task<CategoryInfo?> GetCategoryAsync(string slug, CancellationToken ct);
}

/// <param name="CategoryIds">The category itself plus all of its descendants.</param>
public sealed record CategoryInfo(
    Guid Id, string Slug, bool IsLeaf, IReadOnlyList<SpecDefinition> SpecSchema, IReadOnlyList<Guid> CategoryIds);

/// <summary>Already-validated search input. Spec keys are whitelisted against the category schema.</summary>
/// <param name="Text">
/// The raw search text. The reader turns it into a prefix tsquery; text without any word behaves like no text.
/// </param>
public sealed record ProductSearchCriteria(
    IReadOnlyList<Guid>? CategoryIds,
    string? Text,
    IReadOnlyList<string> Brands,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool InStockOnly,
    bool FeaturedOnly,
    IReadOnlyList<(SpecDefinition Def, SpecFilter Filter)> Specs,
    ProductSort Sort,
    int Page,
    int PageSize);
