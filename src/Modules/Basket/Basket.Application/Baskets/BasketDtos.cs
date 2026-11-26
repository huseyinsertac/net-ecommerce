using System.Text.Json.Serialization;

namespace Basket.Application.Baskets;

/// <summary>
/// The basket with live catalog data. <see cref="Subtotal"/> counts only lines without an issue; <see cref="ItemCount"/>
/// counts every unit.
/// </summary>
public sealed record BasketDto(IReadOnlyList<BasketLineDto> Lines, int ItemCount, decimal Subtotal, bool HasIssues)
{
    public static BasketDto Empty { get; } = new([], 0, 0m, false);
}

/// <summary>
/// One line. For an <see cref="BasketLineIssue.Unavailable"/> line only <see cref="VariantId"/> and <see cref="Quantity"/>
/// are guaranteed. <see cref="LineTotal"/> is set only when the line has no issue.
/// </summary>
public sealed record BasketLineDto(
    Guid VariantId,
    string? ProductSlug,
    string? ProductName,
    string? Brand,
    string? Sku,
    string? OptionsLabel,
    string? ThumbnailUrl,
    decimal? UnitPrice,
    int Quantity,
    int Available,
    decimal? LineTotal,
    BasketLineIssue? Issue);

/// <summary>Why a line cannot be bought as it stands; serialized as "unavailable" or "insufficientStock".</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BasketLineIssue>))]
public enum BasketLineIssue
{
    /// <summary>The variant no longer exists or its product is not Active.</summary>
    [JsonStringEnumMemberName("unavailable")] Unavailable,

    /// <summary>Fewer units are available than the line's quantity.</summary>
    [JsonStringEnumMemberName("insufficientStock")] InsufficientStock,
}
