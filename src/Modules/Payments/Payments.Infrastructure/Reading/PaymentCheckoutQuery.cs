using Microsoft.EntityFrameworkCore;
using Payments.Contracts.Payments;
using Payments.Domain.Payments;
using Payments.Infrastructure.Persistence;

namespace Payments.Infrastructure.Reading;

internal sealed class PaymentCheckoutQuery(PaymentsDbContext db) : IPaymentCheckoutQuery
{
    public async Task<PaymentCheckoutInfo?> GetByOrderAsync(Guid orderId, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.OrderId == orderId, ct);
        return payment is null
            ? null
            : new PaymentCheckoutInfo(
                payment.Id,
                payment.Status.ToString(),
                payment.Status == PaymentStatus.Pending ? payment.CheckoutUrl : null,
                payment.ExpiresAtUtc);
    }
}
