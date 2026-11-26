using System.Text.Json.Serialization;
using Ordering.Domain.Orders;

namespace Ordering.Application.Orders;

/// <summary>
/// One row of the order history. <see cref="ItemCount"/> counts every unit; <see cref="FirstThumbnailUrl"/> is the thumbnail
/// of the first line that has one.
/// </summary>
public sealed record OrderSummaryDto(
    Guid Id,
    string OrderNumber,
    DateTime PlacedAtUtc,
    [property: JsonConverter(typeof(JsonStringEnumConverter<OrderStatus>))] OrderStatus Status,
    decimal Total,
    int ItemCount,
    string? FirstThumbnailUrl);

/// <summary>An order with its lines, address, totals and status timeline (oldest first).</summary>
public sealed record OrderDto(
    Guid Id,
    string OrderNumber,
    DateTime PlacedAtUtc,
    [property: JsonConverter(typeof(JsonStringEnumConverter<OrderStatus>))] OrderStatus Status,
    [property: JsonConverter(typeof(JsonStringEnumConverter<CancellationReason>))] CancellationReason? CancellationReason,
    IReadOnlyList<OrderLineDto> Lines,
    ShippingAddressDto ShippingAddress,
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    IReadOnlyList<StatusChangeDto> History);

/// <summary>A line as it was bought: name, options and price are snapshots taken at checkout.</summary>
public sealed record OrderLineDto(
    Guid VariantId,
    string ProductName,
    string Sku,
    string OptionsLabel,
    string? ThumbnailUrl,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

/// <param name="Country">ISO 3166-1 alpha-2 code, e.g. "GB".</param>
public sealed record ShippingAddressDto(
    string FullName,
    string Line1,
    string? Line2,
    string City,
    string PostalCode,
    string Country);

/// <param name="Note">The cancellation reason on a Cancelled entry; otherwise null.</param>
public sealed record StatusChangeDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter<OrderStatus>))] OrderStatus Status,
    DateTime AtUtc,
    string? Note);

internal static class OrderMapping
{
    public static OrderSummaryDto ToSummary(this Order order) => new(
        order.Id,
        order.OrderNumber,
        order.PlacedAtUtc,
        order.Status,
        order.Total.Amount,
        order.Lines.Sum(l => l.Quantity),
        order.Lines.OrderBy(l => l.LineNumber).Select(l => l.ThumbnailUrl).FirstOrDefault(t => t is not null));

    public static OrderDto ToDto(this Order order) => new(
        order.Id,
        order.OrderNumber,
        order.PlacedAtUtc,
        order.Status,
        order.CancellationReason,
        [.. order.Lines.OrderBy(l => l.LineNumber).Select(l => new OrderLineDto(
            l.VariantId, l.ProductName, l.Sku, l.OptionsLabel, l.ThumbnailUrl, l.UnitPrice.Amount, l.Quantity,
            l.LineTotal.Amount))],
        new ShippingAddressDto(
            order.ShippingAddress.FullName, order.ShippingAddress.Line1, order.ShippingAddress.Line2,
            order.ShippingAddress.City, order.ShippingAddress.PostalCode, order.ShippingAddress.Country),
        order.Subtotal.Amount,
        order.Discount.Amount,
        order.Total.Amount,
        [.. order.History.Select(h => new StatusChangeDto(h.Status, h.AtUtc, h.Note))]);
}
