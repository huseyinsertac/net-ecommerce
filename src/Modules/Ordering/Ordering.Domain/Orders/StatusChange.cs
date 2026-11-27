namespace Ordering.Domain.Orders;

/// <summary>One entry of an order's history. Note carries the cancellation reason for Cancelled entries.</summary>
public sealed record StatusChange(OrderStatus Status, DateTime AtUtc, string? Note);
