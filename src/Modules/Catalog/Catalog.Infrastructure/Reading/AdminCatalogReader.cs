using System.Text;
using Catalog.Application.Abstractions;
using Catalog.Application.Admin;
using Catalog.Application.Products;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence;
using Catalog.Infrastructure.Persistence.Configurations;
using Catalog.Infrastructure.Persistence.Converters;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Shop.Application.Abstractions.Paging;

namespace Catalog.Infrastructure.Reading;

/// <summary>The admin read side: every status is visible. Dapper for the product list, EF for the rest.</summary>
internal sealed class AdminCatalogReader(CatalogDbContext db) : IAdminCatalogReader
{
    public async Task<PagedResult<AdminProductSummaryDto>> ListProductsAsync(
        AdminProductListCriteria criteria, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        var where = new StringBuilder("TRUE");

        if (criteria.Text is not null)
        {
            // ILIKE's default escape character is the backslash; escape it and the wildcards in the user's text.
            var escaped = criteria.Text.Replace(@"\", @"\\", StringComparison.Ordinal)
                .Replace("%", @"\%", StringComparison.Ordinal)
                .Replace("_", @"\_", StringComparison.Ordinal);
            parameters.Add("pattern", $"%{escaped}%");
            where.Append(" AND (p.name ILIKE @pattern OR p.brand ILIKE @pattern OR EXISTS (")
                .Append("SELECT 1 FROM catalog.product_variants sv WHERE sv.product_id = p.id AND sv.sku ILIKE @pattern))");
        }

        if (criteria.Status is { } status)
        {
            where.Append(" AND p.status = @status");
            parameters.Add("status", status.ToString());
        }

        if (criteria.CategorySlug is not null)
        {
            where.Append(" AND c.slug = @categorySlug");
            parameters.Add("categorySlug", criteria.CategorySlug);
        }

        parameters.Add("limit", criteria.PageSize);
        parameters.Add("offset", (long)(criteria.Page - 1) * criteria.PageSize);

        var sql = $"""
            SELECT count(*)::int
            FROM catalog.products p JOIN catalog.categories c ON c.id = p.category_id
            WHERE {where};

            SELECT p.id AS Id, p.slug AS Slug, p.name AS Name, p.brand AS Brand, c.name AS CategoryName,
                   p.status AS Status, p.is_featured AS IsFeatured, p.image_keys[1] AS ImageKey,
                   v.variant_count AS VariantCount, v.total_stock AS TotalStock, v.total_reserved AS TotalReserved,
                   v.min_price AS MinPrice
            FROM catalog.products p
            JOIN catalog.categories c ON c.id = p.category_id
            CROSS JOIN LATERAL (
                SELECT count(*)::int AS variant_count,
                       coalesce(sum(pv.stock_on_hand), 0)::int AS total_stock,
                       coalesce(sum(pv.stock_reserved), 0)::int AS total_reserved,
                       min(pv.price) AS min_price
                FROM catalog.product_variants pv
                WHERE pv.product_id = p.id) v
            WHERE {where}
            ORDER BY p.name, p.id
            LIMIT @limit OFFSET @offset;
            """;

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            using var grid = await db.Database.GetDbConnection()
                .QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: ct));
            var total = await grid.ReadSingleAsync<int>();
            var items = (await grid.ReadAsync<SummaryRow>())
                .Select(r => new AdminProductSummaryDto(
                    r.Id, r.Slug, r.Name, r.Brand, r.CategoryName, Enum.Parse<ProductStatus>(r.Status).ToDto(), r.IsFeatured,
                    r.VariantCount, r.TotalStock, r.TotalReserved, r.MinPrice ?? 0m,
                    r.ImageKey is null ? string.Empty : CatalogImageUrls.ProductThumb(r.ImageKey)))
                .ToList();
            return new PagedResult<AdminProductSummaryDto>(items, criteria.Page, criteria.PageSize, total);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Tracked on purpose: the version is the <c>xmin</c> shadow property, and after an admin save in the same request the
    /// tracked entity already carries the new version returned by the UPDATE.
    /// </summary>
    public async Task<AdminProductDto?> GetProductAsync(Guid productId, CancellationToken ct)
    {
        var product = await db.Products.Include(p => p.Variants).SingleOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null)
        {
            return null;
        }

        var category = await db.Categories.AsNoTracking().SingleAsync(c => c.Id == product.CategoryId, ct);
        var version = db.Entry(product).Property<uint>(ProductConfiguration.RowVersion).CurrentValue;

        // The same order as the public product page: cheapest first, SKU as tiebreak.
        var variants = product.Variants
            .OrderBy(v => v.Price.Amount)
            .ThenBy(v => v.Sku, StringComparer.Ordinal)
            .ToList();

        return new AdminProductDto(
            product.Id, product.Slug, product.Name, product.Brand, product.Description, category.Id, category.Slug,
            product.Status.ToDto(), product.IsFeatured, [.. product.ImageKeys],
            product.Specs.ToDictionary(kv => kv.Key, kv => SpecValuesJsonConverter.ToJson(kv.Value)),
            [.. category.SpecSchema.Select(s => s.ToDto())],
            [.. product.OptionAxes.Select(a => new OptionAxisDto(a.Key, a.Label,
                [.. variants.Select(v => v.Options.GetValueOrDefault(a.Key)).OfType<string>().Distinct()]))],
            [.. variants.Select(v => new AdminVariantDto(
                v.Id, v.Sku, new Dictionary<string, string>(v.Options), v.Price.Amount, v.StockOnHand, v.StockReserved))],
            version);
    }

    public async Task<IReadOnlyList<AdminCategoryDto>> GetLeafCategoriesAsync(CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var parents = categories.Where(c => c.ParentId is not null).Select(c => c.ParentId!.Value).ToHashSet();
        return [.. categories
            .Where(c => !parents.Contains(c.Id))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ThenBy(c => c.Slug, StringComparer.Ordinal)
            .Select(c => new AdminCategoryDto(c.Id, c.Slug, c.Name, [.. c.SpecSchema.Select(s => s.ToDto())]))];
    }

    public async Task<IReadOnlyList<AdminImageKeyDto>> GetImageKeysAsync(CancellationToken ct)
    {
        var keys = await db.Database
            .SqlQuery<string>($"SELECT DISTINCT k AS \"Value\" FROM catalog.products p CROSS JOIN LATERAL unnest(p.image_keys) AS k")
            .ToListAsync(ct);
        return [.. keys.Order(StringComparer.Ordinal).Select(k => new AdminImageKeyDto(k, CatalogImageUrls.ProductThumb(k)))];
    }

    private sealed class SummaryRow
    {
        public Guid Id { get; init; }
        public string Slug { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Brand { get; init; } = string.Empty;
        public string CategoryName { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public bool IsFeatured { get; init; }
        public string? ImageKey { get; init; }
        public int VariantCount { get; init; }
        public int TotalStock { get; init; }
        public int TotalReserved { get; init; }
        public decimal? MinPrice { get; init; }
    }
}
