using Microsoft.EntityFrameworkCore;
using Ordering.Contracts.Orders;
using Ordering.Infrastructure.Persistence;

namespace Ordering.Infrastructure.Reading;

internal sealed class OrderPaymentQuery(OrderingDbContext db) : IOrderPaymentQuery
{
    public async Task<OrderPaymentDetails?> GetAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            return null;
        }

        return new OrderPaymentDetails(
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            order.CustomerEmail,
            order.Total.Amount,
            order.Total.Currency,
            [.. order.Lines
                .OrderBy(l => l.LineNumber)
                .Select(l => new OrderPaymentLine(
                    string.IsNullOrEmpty(l.OptionsLabel) ? l.ProductName : $"{l.ProductName} ({l.OptionsLabel})",
                    l.Quantity,
                    l.UnitPrice.Amount))]);
    }
}
