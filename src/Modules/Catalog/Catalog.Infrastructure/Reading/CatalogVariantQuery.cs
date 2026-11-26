using System.Data.Common;
using System.Text.Json;
using Catalog.Application.Products;
using Catalog.Contracts.Variants;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence;
using Catalog.Infrastructure.Persistence.Converters;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure.Reading;

/// <summary>
/// Cross-module read of live variant data: one Dapper query for any number of ids. The option label is built here
/// because only Catalog knows the axis order. Non-active products are returned too, flagged not purchasable.
/// </summary>
internal sealed class CatalogVariantQuery(CatalogDbContext db) : ICatalogVariantQuery
{
    private const string Separator = " · ";

    private const string Sql = $"""
        SELECT v.id AS VariantId, p.id AS ProductId, p.slug AS ProductSlug, p.name AS ProductName, p.brand AS Brand,
               v.sku AS Sku, v.price AS UnitPrice, v.stock_on_hand - v.stock_reserved AS Available,
               p.image_keys[1] AS ImageKey, (p.status = {SearchSql.Active}) AS IsPurchasable,
               v.options::text AS OptionsJson, p.option_axes::text AS AxesJson
        FROM catalog.product_variants v
        JOIN catalog.products p ON p.id = v.product_id
        WHERE v.id = ANY(@ids)
        """;

    private const string SampleSql = $"""
        SELECT v.id AS VariantId, p.id AS ProductId, p.slug AS ProductSlug, p.name AS ProductName, p.brand AS Brand,
               v.sku AS Sku, v.price AS UnitPrice, v.stock_on_hand - v.stock_reserved AS Available,
               p.image_keys[1] AS ImageKey, (p.status = {SearchSql.Active}) AS IsPurchasable,
               v.options::text AS OptionsJson, p.option_axes::text AS AxesJson
        FROM catalog.products p
        CROSS JOIN LATERAL (
            SELECT * FROM catalog.product_variants pv WHERE pv.product_id = p.id ORDER BY pv.sku LIMIT 1
        ) v
        WHERE p.is_featured AND p.status = {SearchSql.Active}
        ORDER BY p.name, p.id
        LIMIT @count
        """;

    public async Task<IReadOnlyList<VariantSnapshot>> GetSampleAsync(int count, CancellationToken ct)
    {
        if (count < 1)
        {
            return [];
        }

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            DbConnection connection = db.Database.GetDbConnection();
            var rows = await connection.QueryAsync<Row>(new CommandDefinition(
                SampleSql, new { count }, cancellationToken: ct));
            return [.. rows.Select(ToSnapshot)];
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public async Task<IReadOnlyDictionary<Guid, VariantSnapshot>> GetAsync(
        IReadOnlyCollection<Guid> variantIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(variantIds);
        if (variantIds.Count > ICatalogVariantQuery.MaxIds)
        {
            throw new ArgumentException(
                $"At most {ICatalogVariantQuery.MaxIds} variant ids per call.", nameof(variantIds));
        }

        if (variantIds.Count == 0)
        {
            return new Dictionary<Guid, VariantSnapshot>();
        }

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            DbConnection connection = db.Database.GetDbConnection();
            var rows = await connection.QueryAsync<Row>(new CommandDefinition(
                Sql, new { ids = variantIds.Distinct().ToArray() }, cancellationToken: ct));
            return rows.ToDictionary(r => r.VariantId, ToSnapshot);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static VariantSnapshot ToSnapshot(Row r)
    {
        var axes = JsonSerializer.Deserialize<List<OptionAxis>>(r.AxesJson, CatalogJson.Options) ?? [];
        var options = JsonSerializer.Deserialize<Dictionary<string, string>>(r.OptionsJson, CatalogJson.Options) ?? [];
        var label = string.Join(Separator, axes.Select(a => options.GetValueOrDefault(a.Key)).OfType<string>());

        return new VariantSnapshot(
            r.VariantId, r.ProductId, r.ProductSlug, r.ProductName, r.Brand, r.Sku, label, r.UnitPrice,
            Math.Max(0, r.Available),
            r.ImageKey is null ? null : CatalogImageUrls.ProductThumb(r.ImageKey),
            r.IsPurchasable);
    }

    private sealed class Row
    {
        public Guid VariantId { get; init; }
        public Guid ProductId { get; init; }
        public string ProductSlug { get; init; } = string.Empty;
        public string ProductName { get; init; } = string.Empty;
        public string Brand { get; init; } = string.Empty;
        public string Sku { get; init; } = string.Empty;
        public decimal UnitPrice { get; init; }
        public int Available { get; init; }
        public string? ImageKey { get; init; }
        public bool IsPurchasable { get; init; }
        public string OptionsJson { get; init; } = "{}";
        public string AxesJson { get; init; } = "[]";
    }
}
