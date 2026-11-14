using System.Text;
using Catalog.Application.Abstractions;
using Catalog.Application.Products;
using Catalog.Domain.Categories;
using Dapper;

namespace Catalog.Infrastructure.Reading;

/// <summary>
/// Builds the product search SQL. Only fixed SQL fragments are concatenated; every user-supplied value (text, brands,
/// prices, spec keys and values) is a parameter. Spec keys are additionally whitelisted by the handler.
/// </summary>
internal static class SearchSql
{
    public const string Active = "'Active'";

    public static (string Sql, DynamicParameters Parameters) Build(ProductSearchCriteria criteria)
    {
        var parameters = new DynamicParameters();
        var where = new StringBuilder($"p.status = {Active}");
        var tsQuery = TsQueryBuilder.Build(criteria.Text);

        if (criteria.CategoryIds is not null)
        {
            where.Append(" AND p.category_id = ANY(@categoryIds)");
            parameters.Add("categoryIds", criteria.CategoryIds.ToArray());
        }

        if (tsQuery is not null)
        {
            where.Append(" AND p.search_vector @@ to_tsquery('english', @tsQuery)");
            parameters.Add("tsQuery", tsQuery);
        }

        if (criteria.Brands.Count > 0)
        {
            where.Append(" AND lower(p.brand) = ANY(@brands)");
            parameters.Add("brands", criteria.Brands.Select(b => b.ToLowerInvariant()).ToArray());
        }

        if (criteria.FeaturedOnly)
        {
            where.Append(" AND p.is_featured");
        }

        // Price range and stock must hold for the same variant.
        if (criteria.MinPrice.HasValue || criteria.MaxPrice.HasValue || criteria.InStockOnly)
        {
            where.Append(" AND EXISTS (SELECT 1 FROM catalog.product_variants fv WHERE fv.product_id = p.id");
            if (criteria.MinPrice.HasValue)
            {
                where.Append(" AND fv.price >= @minPrice");
                parameters.Add("minPrice", criteria.MinPrice.Value);
            }

            if (criteria.MaxPrice.HasValue)
            {
                where.Append(" AND fv.price <= @maxPrice");
                parameters.Add("maxPrice", criteria.MaxPrice.Value);
            }

            if (criteria.InStockOnly)
            {
                where.Append(" AND fv.stock_on_hand - fv.stock_reserved > 0");
            }

            where.Append(')');
        }

        for (var i = 0; i < criteria.Specs.Count; i++)
        {
            AppendSpec(where, parameters, i, criteria.Specs[i].Def, criteria.Specs[i].Filter);
        }

        var rank = tsQuery is null ? "0::real" : "ts_rank(p.search_vector, to_tsquery('english', @tsQuery))";
        var sort = criteria.Sort == ProductSort.Relevance && tsQuery is null ? ProductSort.Featured : criteria.Sort;
        var orderBy = sort switch
        {
            ProductSort.Relevance => "rank DESC",
            ProductSort.PriceAsc => "pricefrom ASC",
            ProductSort.PriceDesc => "pricefrom DESC",
            ProductSort.Newest => "p.created_at_utc DESC",
            _ => "p.is_featured DESC",
        };

        parameters.Add("limit", criteria.PageSize);
        parameters.Add("offset", (long)(criteria.Page - 1) * criteria.PageSize);

        var sql = $"""
            SELECT count(*)::int FROM catalog.products p WHERE {where};

            SELECT p.id AS Id, p.slug AS Slug, p.name AS Name, p.brand AS Brand, c.slug AS CategorySlug,
                   p.image_keys[1] AS ImageKey, v.price_from AS PriceFrom, v.price_to AS PriceTo,
                   v.in_stock AS InStock, p.is_featured AS IsFeatured, {rank} AS rank
            FROM catalog.products p
            JOIN catalog.categories c ON c.id = p.category_id
            CROSS JOIN LATERAL (
                SELECT min(pv.price) AS price_from, max(pv.price) AS price_to,
                       coalesce(bool_or(pv.stock_on_hand - pv.stock_reserved > 0), false) AS in_stock
                FROM catalog.product_variants pv
                WHERE pv.product_id = p.id) v
            WHERE {where}
            ORDER BY {orderBy}, p.name, p.id
            LIMIT @limit OFFSET @offset;
            """;

        return (sql, parameters);
    }

    private static void AppendSpec(
        StringBuilder where, DynamicParameters parameters, int index, SpecDefinition definition, SpecFilter filter)
    {
        var key = $"specKey{index}";
        parameters.Add(key, definition.Key);

        if (definition.Type == SpecType.Number)
        {
            if (filter.Min.HasValue)
            {
                where.Append($" AND (p.specs->>@{key})::numeric >= @specMin{index}");
                parameters.Add($"specMin{index}", filter.Min.Value);
            }

            if (filter.Max.HasValue)
            {
                where.Append($" AND (p.specs->>@{key})::numeric <= @specMax{index}");
                parameters.Add($"specMax{index}", filter.Max.Value);
            }

            return;
        }

        // JSONB containment, so the GIN jsonb_path_ops index on specs can serve it. Booleans stay JSON booleans.
        var cast = definition.Type == SpecType.Boolean ? "boolean" : "text";
        var alternatives = new List<string>();
        for (var j = 0; j < filter.Values.Count; j++)
        {
            var value = $"specValue{index}_{j}";
            alternatives.Add($"p.specs @> jsonb_build_object(@{key}::text, @{value}::{cast})");
            parameters.Add(value, definition.Type == SpecType.Boolean ? filter.Values[j] == "true" : filter.Values[j]);
        }

        where.Append($" AND ({string.Join(" OR ", alternatives)})");
    }
}
