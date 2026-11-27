using System.Text.Json.Serialization;
using Ordering.Application.Orders;
using Ordering.Domain.Orders;

namespace Ordering.Application.Admin;

/// <summary>One row of the admin order list.</summary>
public sealed record AdminOrderSummaryDto(
    Guid Id,
    string OrderNumber,
    DateTime PlacedAtUtc,
    [property: JsonConverter(typeof(JsonStringEnumConverter<OrderStatus>))] OrderStatus Status,
    string CustomerEmail,
    int ItemCount,
    decimal Total);

/// <summary>
/// An order as the customer sees it, plus who owns it and whether a refund is still owed
/// (the order was cancelled after it was paid).
/// </summary>
public sealed record AdminOrderDto(
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
    IReadOnlyList<StatusChangeDto> History,
    string CustomerEmail,
    Guid CustomerId,
    bool RefundRequired);

internal static class AdminOrderMapping
{
    public static AdminOrderSummaryDto ToAdminSummary(this Order order) => new(
        order.Id, order.OrderNumber, order.PlacedAtUtc, order.Status, order.CustomerEmail,
        order.Lines.Sum(l => l.Quantity), order.Total.Amount);

    public static AdminOrderDto ToAdminDto(this Order order)
    {
        var dto = order.ToDto();
        return new AdminOrderDto(
            dto.Id, dto.OrderNumber, dto.PlacedAtUtc, dto.Status, dto.CancellationReason, dto.Lines,
            dto.ShippingAddress, dto.Subtotal, dto.Discount, dto.Total, dto.History,
            order.CustomerEmail, order.CustomerId, order.RefundRequired);
    }
}
