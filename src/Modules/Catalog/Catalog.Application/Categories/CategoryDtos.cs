namespace Catalog.Application.Categories;

public sealed record CategoryNodeDto(
    string Slug, string Name, string Icon, string ImageUrl, int ProductCount, IReadOnlyList<CategoryNodeDto> Children);

public sealed record FacetOptionDto(string Value, int Count);

/// <param name="Type">"number" | "enum" | "boolean".</param>
public sealed record SpecFacetDto(
    string Key, string Label, string Type, string? Unit, decimal? Min, decimal? Max, IReadOnlyList<FacetOptionDto> Options);

public sealed record CategoryFacetsDto(
    string Slug, string Name, IReadOnlyList<FacetOptionDto> Brands, decimal MinPrice, decimal MaxPrice,
    IReadOnlyList<SpecFacetDto> Specs);
