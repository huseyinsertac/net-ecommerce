using Microsoft.EntityFrameworkCore;
using Payments.Application.Abstractions;
using Payments.Domain.Payments;

namespace Payments.Infrastructure.Persistence;

internal sealed class PaymentRepository(PaymentsDbContext db) : IPaymentRepository
{
    public Task<bool> ExistsForOrderAsync(Guid orderId, CancellationToken ct) =>
        db.Payments.AnyAsync(p => p.OrderId == orderId, ct);

    public Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken ct) =>
        db.Payments.SingleOrDefaultAsync(p => p.Id == paymentId, ct);

    public Task<Payment?> GetByOrderAsync(Guid orderId, CancellationToken ct) =>
        db.Payments.SingleOrDefaultAsync(p => p.OrderId == orderId, ct);

    public Task<Payment?> GetBySessionIdAsync(string sessionId, CancellationToken ct) =>
        db.Payments.SingleOrDefaultAsync(p => p.ProviderSessionId == sessionId, ct);

    public async Task<IReadOnlyList<Guid>> GetOverduePendingIdsAsync(DateTime nowUtc, int max, CancellationToken ct) =>
        await db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Pending && p.ExpiresAtUtc <= nowUtc)
            .OrderBy(p => p.ExpiresAtUtc)
            .Select(p => p.Id)
            .Take(max)
            .ToListAsync(ct);

    public void Add(Payment payment) => db.Payments.Add(payment);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
