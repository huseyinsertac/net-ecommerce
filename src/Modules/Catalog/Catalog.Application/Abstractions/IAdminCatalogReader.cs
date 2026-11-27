using Catalog.Application.Admin;
using Catalog.Domain.Products;
using Shop.Application.Abstractions.Paging;

namespace Catalog.Application.Abstractions;

/// <summary>The admin read side. Unlike <see cref="ICatalogReader"/> it sees products in every status.</summary>
public interface IAdminCatalogReader
{
    Task<PagedResult<AdminProductSummaryDto>> ListProductsAsync(AdminProductListCriteria criteria, CancellationToken ct);

    Task<AdminProductDto?> GetProductAsync(Guid productId, CancellationToken ct);

    Task<IReadOnlyList<AdminCategoryDto>> GetLeafCategoriesAsync(CancellationToken ct);

    Task<IReadOnlyList<AdminImageKeyDto>> GetImageKeysAsync(CancellationToken ct);
}

/// <param name="Text">Matched case-insensitively as a substring of the name, the brand or any SKU.</param>
public sealed record AdminProductListCriteria(
    string? Text, ProductStatus? Status, string? CategorySlug, int Page, int PageSize);
