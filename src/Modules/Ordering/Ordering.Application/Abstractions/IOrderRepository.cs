using Ordering.Domain.Orders;
using Shop.Application.Abstractions.Paging;

namespace Ordering.Application.Abstractions;

/// <summary>Loads and saves the <see cref="Order"/> aggregate, always with its lines.</summary>
public interface IOrderRepository
{
    /// <summary>The order, tracked so changes to it are saved; null when it does not exist.</summary>
    Task<Order?> GetAsync(Guid orderId, CancellationToken ct);

    /// <summary>The customer's order placed with this idempotency key, if any.</summary>
    Task<Order?> GetByIdempotencyKeyAsync(Guid customerId, Guid idempotencyKey, CancellationToken ct);

    /// <summary>The order when it exists and belongs to the customer; otherwise null. Read-only (not tracked).</summary>
    Task<Order?> GetForCustomerAsync(Guid customerId, Guid orderId, CancellationToken ct);

    /// <summary>One page of the customer's orders, newest first. Read-only (not tracked).</summary>
    Task<PagedResult<Order>> GetPageForCustomerAsync(Guid customerId, int page, int pageSize, CancellationToken ct);

    /// <summary>The next value of <c>ordering.order_number_seq</c>. Values are never reused, even if the order is not saved.</summary>
    Task<long> NextOrderNumberAsync(CancellationToken ct);

    void Add(Order order);

    /// <summary>Saves all changes, plus the outbox rows published by domain-event handlers, in one transaction.</summary>
    /// <exception cref="DuplicateIdempotencyKeyException">Another request placed an order with the same key first.</exception>
    Task SaveChangesAsync(CancellationToken ct);
}
