using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Ordering.Application.Admin;

/// <summary>Paid to Shipped.</summary>
public sealed record ShipOrder(Guid OrderId) : ICommand<AdminOrderDto>;

/// <summary>Shipped to Delivered.</summary>
public sealed record DeliverOrder(Guid OrderId) : ICommand<AdminOrderDto>;

internal sealed class ShipOrderHandler(IOrderRepository orders, TimeProvider clock) : ICommandHandler<ShipOrder, AdminOrderDto>
{
    public Task<Result<AdminOrderDto>> Handle(ShipOrder command, CancellationToken ct) =>
        AdminOrderTransition.ApplyAsync(orders, command.OrderId, OrderStatus.Shipped, o => o.Ship(clock.GetUtcNow().UtcDateTime), ct);
}

internal sealed class DeliverOrderHandler(IOrderRepository orders, TimeProvider clock) : ICommandHandler<DeliverOrder, AdminOrderDto>
{
    public Task<Result<AdminOrderDto>> Handle(DeliverOrder command, CancellationToken ct) =>
        AdminOrderTransition.ApplyAsync(orders, command.OrderId, OrderStatus.Delivered, o => o.Deliver(clock.GetUtcNow().UtcDateTime), ct);
}

internal static class AdminOrderTransition
{
    public static async Task<Result<AdminOrderDto>> ApplyAsync(
        IOrderRepository orders, Guid orderId, OrderStatus target, Func<Order, Result> transition, CancellationToken ct)
    {
        var order = await orders.GetAsync(orderId, ct);
        if (order is null)
        {
            return OrderErrors.OrderNotFound();
        }

        var result = transition(order);
        if (result.IsFailure)
        {
            return result.Error;
        }

        try
        {
            await orders.SaveChangesAsync(ct);
        }
        catch (OrderConcurrencyException)
        {
            // Another click won the race: reload and report the order's real state as an invalid transition.
            var current = await orders.GetReadOnlyAsync(orderId, ct);
            return current is null ? OrderErrors.OrderNotFound() : OrderErrors.InvalidTransition(current.Status, target);
        }

        return order.ToAdminDto();
    }
}
