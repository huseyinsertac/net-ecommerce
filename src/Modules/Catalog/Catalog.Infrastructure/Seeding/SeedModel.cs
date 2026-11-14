using System.Text.Json;

namespace Catalog.Infrastructure.Seeding;

/// <summary>The shape of <c>catalog-seed.json</c>.</summary>
internal sealed record SeedFile(IReadOnlyList<SeedCategory> Categories, IReadOnlyList<SeedProduct> Products);

internal sealed record SeedCategory(
    string Slug, string Name, string Icon, string? Parent, int SortOrder, IReadOnlyList<SeedSpec> Specs);

internal sealed record SeedSpec(
    string Key, string Label, string Type, string? Unit, bool Filterable, IReadOnlyList<string> Options);

internal sealed record SeedProduct(
    string Slug, string Name, string Brand, string Category, string Description, IReadOnlyList<string> Images,
    IReadOnlyDictionary<string, JsonElement> Specs, IReadOnlyList<SeedAxis> OptionAxes,
    IReadOnlyList<SeedVariant> Variants, bool Featured, bool Published, DateTime CreatedAt);

internal sealed record SeedAxis(string Key, string Label);

internal sealed record SeedVariant(string Sku, IReadOnlyDictionary<string, string> Options, decimal Price, int Stock);
