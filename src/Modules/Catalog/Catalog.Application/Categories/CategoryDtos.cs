using System.Text.Json.Serialization;

namespace Catalog.Application.Categories;

public sealed record CategoryNodeDto(
    string Slug, string Name, string Icon, string ImageUrl, int ProductCount, IReadOnlyList<CategoryNodeDto> Children);

public sealed record FacetOptionDto(string Value, int Count);

/// <summary>How a spec facet is filtered; serialized as "number", "enum" or "boolean".</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SpecFacetType>))]
public enum SpecFacetType
{
    [JsonStringEnumMemberName("number")] Number,
    [JsonStringEnumMemberName("enum")] Enum,
    [JsonStringEnumMemberName("boolean")] Boolean,
}

public sealed record SpecFacetDto(
    string Key, string Label, SpecFacetType Type, string? Unit, decimal? Min, decimal? Max, IReadOnlyList<FacetOptionDto> Options);

public sealed record CategoryFacetsDto(
    string Slug, string Name, IReadOnlyList<FacetOptionDto> Brands, decimal MinPrice, decimal MaxPrice,
    IReadOnlyList<SpecFacetDto> Specs);
