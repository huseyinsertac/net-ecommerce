using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;
using Shop.Application.Abstractions.Paging;

namespace Ordering.Infrastructure.Persistence;

internal sealed class OrderRepository(OrderingDbContext db) : IOrderRepository
{
    private IQueryable<Order> ReadOnlyOrders => db.Orders.AsNoTracking().Include(o => o.Lines);

    public Task<Order?> GetAsync(Guid orderId, CancellationToken ct) =>
        db.Orders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == orderId, ct);

    public Task<Order?> GetByIdempotencyKeyAsync(Guid customerId, Guid idempotencyKey, CancellationToken ct) =>
        ReadOnlyOrders.SingleOrDefaultAsync(o => o.CustomerId == customerId && o.IdempotencyKey == idempotencyKey, ct);

    public Task<Order?> GetForCustomerAsync(Guid customerId, Guid orderId, CancellationToken ct) =>
        ReadOnlyOrders.SingleOrDefaultAsync(o => o.Id == orderId && o.CustomerId == customerId, ct);

    public async Task<PagedResult<Order>> GetPageForCustomerAsync(Guid customerId, int page, int pageSize, CancellationToken ct)
    {
        var mine = db.Orders.AsNoTracking().Where(o => o.CustomerId == customerId);
        var total = await mine.CountAsync(ct);
        var items = await mine
            .OrderByDescending(o => o.PlacedAtUtc)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(o => o.Lines)
            .AsSplitQuery()
            .ToListAsync(ct);
        return new PagedResult<Order>(items, page, pageSize, total);
    }

    public async Task<long> NextOrderNumberAsync(CancellationToken ct)
    {
        // The sequence name comes from code, never from input.
#pragma warning disable EF1002
        return await db.Database
            .SqlQueryRaw<long>($"SELECT nextval('{db.Schema}.{OrderingDbContext.OrderNumberSequence}') AS \"Value\"")
            .SingleAsync(ct);
#pragma warning restore EF1002
    }

    public void Add(Order order) => db.Orders.Add(order);

    /// <summary>Saves, translating the idempotency-key race into the exception the application layer understands.</summary>
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           {
                                               SqlState: PostgresErrorCodes.UniqueViolation,
                                               ConstraintName: OrderConfiguration.IdempotencyIndex,
                                           })
        {
            // Forget the losing order (and its outbox row) so a reload in this scope reads the winner.
            db.ChangeTracker.Clear();
            throw new DuplicateIdempotencyKeyException(ex);
        }
    }
}
