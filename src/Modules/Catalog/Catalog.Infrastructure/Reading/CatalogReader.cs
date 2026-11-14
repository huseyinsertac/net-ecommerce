using System.Data.Common;
using System.Globalization;
using Catalog.Application.Abstractions;
using Catalog.Application.Categories;
using Catalog.Application.Products;
using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Shop.Application.Abstractions.Paging;

namespace Catalog.Infrastructure.Reading;

/// <summary>
/// SQL read side (CQRS-lite): Dapper for search and facets, EF no-tracking for the category tree and product detail.
/// Uses the scoped <see cref="CatalogDbContext"/>'s connection so reads share the module's connection settings.
/// Only Active products are ever visible.
/// </summary>
internal sealed class CatalogReader(CatalogDbContext db) : ICatalogReader
{
    private const string ActiveProducts =
        $"FROM catalog.products p WHERE p.status = {SearchSql.Active} AND p.category_id = ANY(@ids)";

    public async Task<PagedResult<ProductSummaryDto>> SearchAsync(ProductSearchCriteria criteria, CancellationToken ct)
    {
        var (sql, parameters) = SearchSql.Build(criteria);
        return await WithConnection(async connection =>
        {
            using var grid = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: ct));
            var total = await grid.ReadSingleAsync<int>();
            var rows = await grid.ReadAsync<SummaryRow>();
            var items = rows
                .Select(r => new ProductSummaryDto(
                    r.Id, r.Slug, r.Name, r.Brand, r.CategorySlug,
                    r.ImageKey is null ? string.Empty : CatalogImageUrls.ProductThumb(r.ImageKey),
                    r.PriceFrom ?? 0m, r.PriceTo ?? 0m, r.InStock, r.IsFeatured))
                .ToList();
            return new PagedResult<ProductSummaryDto>(items, criteria.Page, criteria.PageSize, total);
        }, ct);
    }

    public async Task<ProductDetailDto?> GetProductAsync(string slug, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking()
            .Include(p => p.Variants)
            .SingleOrDefaultAsync(p => p.Slug == slug && p.Status == ProductStatus.Active, ct);
        if (product is null)
        {
            return null;
        }

        var categories = await LoadCategoriesAsync(ct);
        var byId = categories.ToDictionary(c => c.Id);
        var category = byId[product.CategoryId];

        var breadcrumb = new List<CategoryRefDto>();
        for (Category? current = category; current is not null;
             current = current.ParentId is { } parentId ? byId.GetValueOrDefault(parentId) : null)
        {
            breadcrumb.Insert(0, new CategoryRefDto(current.Slug, current.Name));
        }

        var specs = category.SpecSchema
            .Where(s => product.Specs.ContainsKey(s.Key))
            .Select(s => new SpecRowDto(s.Key, s.Label, FormatSpec(s, product.Specs[s.Key])))
            .ToList();

        // No stored variant position: present variants cheapest first (SKU as tiebreak), and derive axis value order from that.
        var variants = product.Variants
            .OrderBy(v => v.Price.Amount)
            .ThenBy(v => v.Sku, StringComparer.Ordinal)
            .ToList();

        var axes = product.OptionAxes
            .Select(a => new OptionAxisDto(a.Key, a.Label,
                [.. variants.Select(v => v.Options.GetValueOrDefault(a.Key)).OfType<string>().Distinct()]))
            .ToList();

        return new ProductDetailDto(
            product.Id, product.Slug, product.Name, product.Brand, product.Description, breadcrumb,
            [.. product.ImageKeys.Select(k => new ProductImageDto(CatalogImageUrls.Product(k), CatalogImageUrls.ProductThumb(k)))],
            specs,
            axes,
            [.. variants.Select(v => new VariantDto(
                v.Id, v.Sku, new Dictionary<string, string>(v.Options), v.Price.Amount, Math.Max(0, v.Available)))]);
    }

    public async Task<IReadOnlyList<CategoryNodeDto>> GetCategoryTreeAsync(CancellationToken ct)
    {
        var categories = await LoadCategoriesAsync(ct);
        var direct = await WithConnection(async connection =>
            (await connection.QueryAsync<(Guid CategoryId, int Count)>(new CommandDefinition(
                $"SELECT category_id, count(*)::int FROM catalog.products WHERE status = {SearchSql.Active} GROUP BY category_id",
                cancellationToken: ct)))
            .ToDictionary(r => r.CategoryId, r => r.Count), ct);

        var children = categories.ToLookup(c => c.ParentId);

        CategoryNodeDto Build(Category c)
        {
            var kids = children[c.Id].Select(Build).ToList();
            var count = direct.GetValueOrDefault(c.Id) + kids.Sum(k => k.ProductCount);
            return new CategoryNodeDto(c.Slug, c.Name, c.Icon, CatalogImageUrls.Category(c.Slug), count, kids);
        }

        return [.. children[null].Select(Build)];
    }

    public async Task<CategoryFacetsDto?> GetFacetsAsync(string slug, CancellationToken ct)
    {
        var categories = await LoadCategoriesAsync(ct);
        var category = categories.FirstOrDefault(c => c.Slug == slug);
        if (category is null)
        {
            return null;
        }

        var info = ToInfo(category, categories);
        var ids = info.CategoryIds.ToArray();
        var specDefs = info.IsLeaf ? category.SpecSchema.Where(s => s.Filterable).ToList() : [];
        var numberKeys = specDefs.Where(s => s.Type == SpecType.Number).Select(s => s.Key).ToArray();
        var valueKeys = specDefs.Where(s => s.Type != SpecType.Number).Select(s => s.Key).ToArray();

        var sql = $"""
            SELECT p.brand AS Value, count(*)::int AS Count {ActiveProducts} GROUP BY p.brand ORDER BY p.brand;

            SELECT min(v.price) AS Min, max(v.price) AS Max
            FROM catalog.product_variants v
            WHERE v.product_id IN (SELECT p.id {ActiveProducts});

            SELECT e.key AS Key, min(e.value::numeric) AS Min, max(e.value::numeric) AS Max
            FROM catalog.products p CROSS JOIN LATERAL jsonb_each_text(p.specs) e
            WHERE p.status = {SearchSql.Active} AND p.category_id = ANY(@ids) AND e.key = ANY(@numberKeys)
            GROUP BY e.key;

            SELECT e.key AS Key, e.value AS Value, count(*)::int AS Count
            FROM catalog.products p CROSS JOIN LATERAL jsonb_each_text(p.specs) e
            WHERE p.status = {SearchSql.Active} AND p.category_id = ANY(@ids) AND e.key = ANY(@valueKeys)
            GROUP BY e.key, e.value
            ORDER BY e.value;
            """;

        return await WithConnection(async connection =>
        {
            using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
                sql, new { ids, numberKeys, valueKeys }, cancellationToken: ct));
            var brands = (await grid.ReadAsync<FacetRow>()).Select(r => new FacetOptionDto(r.Value, r.Count)).ToList();
            var price = await grid.ReadSingleAsync<RangeRow>();
            var ranges = (await grid.ReadAsync<RangeRow>()).ToDictionary(r => r.Key!);
            var counts = (await grid.ReadAsync<FacetRow>()).ToLookup(r => r.Key!);

            var specs = specDefs.Select(s => s.Type switch
            {
                SpecType.Number => new SpecFacetDto(s.Key, s.Label, "number", s.Unit,
                    ranges.GetValueOrDefault(s.Key)?.Min, ranges.GetValueOrDefault(s.Key)?.Max, []),
                SpecType.Enum => new SpecFacetDto(s.Key, s.Label, "enum", s.Unit, null, null,
                    [.. s.Options.Select(o => new FacetOptionDto(o, CountOf(counts[s.Key], o)))]),
                SpecType.Boolean => new SpecFacetDto(s.Key, s.Label, "boolean", s.Unit, null, null,
                    [new FacetOptionDto("true", CountOf(counts[s.Key], "true")),
                     new FacetOptionDto("false", CountOf(counts[s.Key], "false"))]),
                // Filterable free text: offered like an enum over the values that actually occur.
                _ => new SpecFacetDto(s.Key, s.Label, "enum", s.Unit, null, null,
                    [.. counts[s.Key].Select(r => new FacetOptionDto(r.Value, r.Count))]),
            }).ToList();

            return new CategoryFacetsDto(category.Slug, category.Name, brands, price.Min ?? 0m, price.Max ?? 0m, specs);
        }, ct);
    }

    public async Task<CategoryInfo?> GetCategoryAsync(string slug, CancellationToken ct)
    {
        var categories = await LoadCategoriesAsync(ct);
        var category = categories.FirstOrDefault(c => c.Slug == slug);
        return category is null ? null : ToInfo(category, categories);
    }

    /// <summary>The category table is small (tens of rows), so tree walks happen in memory.</summary>
    private async Task<List<Category>> LoadCategoriesAsync(CancellationToken ct) =>
        await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);

    private static CategoryInfo ToInfo(Category category, IReadOnlyList<Category> all)
    {
        var children = all.ToLookup(c => c.ParentId);
        var ids = new List<Guid>();
        var pending = new Queue<Guid>([category.Id]);
        while (pending.TryDequeue(out var id))
        {
            ids.Add(id);
            foreach (var child in children[id])
            {
                pending.Enqueue(child.Id);
            }
        }

        return new CategoryInfo(category.Id, category.Slug, !children[category.Id].Any(), category.SpecSchema, ids);
    }

    private static int CountOf(IEnumerable<FacetRow> rows, string value) =>
        rows.FirstOrDefault(r => r.Value == value)?.Count ?? 0;

    private static string FormatSpec(SpecDefinition definition, SpecValue value) => value switch
    {
        NumberValue n => string.IsNullOrEmpty(definition.Unit)
            ? FormatNumber(n.Value)
            : $"{FormatNumber(n.Value)} {definition.Unit}",
        BoolValue b => b.Value ? "Yes" : "No",
        TextValue t => t.Value,
        _ => string.Empty,
    };

    private static string FormatNumber(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    private async Task<T> WithConnection<T>(Func<DbConnection, Task<T>> read, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            return await read(db.Database.GetDbConnection());
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private sealed class SummaryRow
    {
        public Guid Id { get; init; }
        public string Slug { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Brand { get; init; } = string.Empty;
        public string CategorySlug { get; init; } = string.Empty;
        public string? ImageKey { get; init; }
        public decimal? PriceFrom { get; init; }
        public decimal? PriceTo { get; init; }
        public bool InStock { get; init; }
        public bool IsFeatured { get; init; }
    }

    private sealed class FacetRow
    {
        public string? Key { get; init; }
        public string Value { get; init; } = string.Empty;
        public int Count { get; init; }
    }

    private sealed class RangeRow
    {
        public string? Key { get; init; }
        public decimal? Min { get; init; }
        public decimal? Max { get; init; }
    }
}
