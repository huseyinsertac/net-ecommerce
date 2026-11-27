using System.Text.Json;
using System.Text.Json.Serialization;
using Catalog.Application.Products;
using Catalog.Domain.Categories;
using Catalog.Domain.Products;

namespace Catalog.Application.Admin;

/// <summary>A product's lifecycle state; serialized as "Draft", "Active" or "Archived". Only Active products are public.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AdminProductStatus>))]
public enum AdminProductStatus { Draft, Active, Archived }

/// <summary>Serialized as "number", "text", "bool" or "enum".</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AdminSpecType>))]
public enum AdminSpecType
{
    [JsonStringEnumMemberName("number")] Number,
    [JsonStringEnumMemberName("text")] Text,
    [JsonStringEnumMemberName("bool")] Bool,
    [JsonStringEnumMemberName("enum")] Enum,
}

public sealed record AdminProductSummaryDto(
    Guid Id, string Slug, string Name, string Brand, string CategoryName, AdminProductStatus Status, bool IsFeatured,
    int VariantCount, int TotalStock, int TotalReserved, decimal MinPrice, string ThumbnailUrl);

/// <param name="Required">Every spec of the category's schema must have a value.</param>
/// <param name="AllowedValues">The options of an enum spec; null for other types.</param>
public sealed record AdminSpecDefinitionDto(
    string Key, string Label, AdminSpecType Type, string? Unit, bool Required, IReadOnlyList<string>? AllowedValues);

public sealed record AdminCategoryDto(Guid Id, string Slug, string Name, IReadOnlyList<AdminSpecDefinitionDto> SpecSchema);

public sealed record AdminVariantDto(
    Guid Id, string Sku, IReadOnlyDictionary<string, string> Options, decimal Price, int StockOnHand, int StockReserved);

/// <param name="Specs">Spec key to its raw value: a JSON number, string or boolean (as sent when saving).</param>
/// <param name="Version">Send it back with every write; a write based on an older version is refused with 409.</param>
public sealed record AdminProductDto(
    Guid Id, string Slug, string Name, string Brand, string Description, Guid CategoryId, string CategorySlug,
    AdminProductStatus Status, bool IsFeatured, IReadOnlyList<string> ImageKeys,
    IReadOnlyDictionary<string, JsonElement> Specs, IReadOnlyList<AdminSpecDefinitionDto> SpecSchema,
    IReadOnlyList<OptionAxisDto> OptionAxes, IReadOnlyList<AdminVariantDto> Variants, uint Version);

public sealed record AdminImageKeyDto(string Key, string ThumbnailUrl);

public sealed record CreatedProductDto(Guid Id, string Slug);

/// <summary>Mappings shared by the admin read side and handlers.</summary>
public static class AdminMapping
{
    public static AdminProductStatus ToDto(this ProductStatus status) => status switch
    {
        ProductStatus.Draft => AdminProductStatus.Draft,
        ProductStatus.Active => AdminProductStatus.Active,
        ProductStatus.Archived => AdminProductStatus.Archived,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown product status."),
    };

    public static AdminSpecDefinitionDto ToDto(this SpecDefinition spec) => new(
        spec.Key,
        spec.Label,
        spec.Type switch
        {
            SpecType.Number => AdminSpecType.Number,
            SpecType.Text => AdminSpecType.Text,
            SpecType.Boolean => AdminSpecType.Bool,
            SpecType.Enum => AdminSpecType.Enum,
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Type, "Unknown spec type."),
        },
        spec.Unit,
        Required: true,
        spec.Type == SpecType.Enum ? [.. spec.Options] : null);

    /// <summary>Parses a status name ("Draft", "Active", "Archived"; any case). Null when it is not one of them.</summary>
    public static ProductStatus? ParseStatus(string? value) =>
        Enum.TryParse<ProductStatus>(value, ignoreCase: true, out var status) && Enum.IsDefined(status)
        && !int.TryParse(value, out _)
            ? status
            : null;
}
