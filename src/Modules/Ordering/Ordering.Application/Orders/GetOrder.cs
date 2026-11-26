using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Ordering.Application.Orders;

/// <summary>One of the customer's orders. Another customer's order is reported as not found.</summary>
public sealed record GetOrder(Guid CustomerId, Guid OrderId) : IQuery<OrderDto>;

internal sealed class GetOrderHandler(IOrderRepository orders) : IQueryHandler<GetOrder, OrderDto>
{
    public async Task<Result<OrderDto>> Handle(GetOrder query, CancellationToken ct)
    {
        var order = await orders.GetForCustomerAsync(query.CustomerId, query.OrderId, ct);
        if (order is null)
        {
            return OrderErrors.OrderNotFound();
        }

        return order.ToDto();
    }
}
