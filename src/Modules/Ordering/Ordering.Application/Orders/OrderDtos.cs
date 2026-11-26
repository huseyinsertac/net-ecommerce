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

/// <summary>Where a checkout stands, as the client polling checkout-status sees it.</summary>
public enum CheckoutState
{
    /// <summary>Still in progress (stock not yet reserved, or the payment outcome not yet applied): keep polling.</summary>
    Pending,

    /// <summary>The stock could not be reserved; the order is cancelled.</summary>
    OutOfStock,

    /// <summary>The payment session is open: send the shopper to the payment URL.</summary>
    ReadyToPay,

    /// <summary>The order is paid (or already further along).</summary>
    Paid,

    /// <summary>The order was cancelled for another reason, given in the reason field.</summary>
    Failed,
}

/// <param name="PaymentUrl">Where the shopper pays; only when <paramref name="State"/> is ReadyToPay.</param>
/// <param name="Reason">The cancellation reason (PaymentFailed, PaymentExpired or Admin); only when <paramref name="State"/> is Failed.</param>
/// <param name="ExpiresAtUtc">When the payment session closes; only when <paramref name="State"/> is ReadyToPay.</param>
public sealed record CheckoutStatusDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter<CheckoutState>))] CheckoutState State,
    string? PaymentUrl = null,
    string? Reason = null,
    DateTime? ExpiresAtUtc = null);

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
