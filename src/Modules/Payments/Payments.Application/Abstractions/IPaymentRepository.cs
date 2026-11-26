using Payments.Domain.Payments;

namespace Payments.Application.Abstractions;

/// <summary>Loads (tracked) and saves the <see cref="Payment"/> aggregate.</summary>
public interface IPaymentRepository
{
    Task<bool> ExistsForOrderAsync(Guid orderId, CancellationToken ct);

    Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken ct);

    Task<Payment?> GetByOrderAsync(Guid orderId, CancellationToken ct);

    Task<Payment?> GetBySessionIdAsync(string sessionId, CancellationToken ct);

    /// <summary>Ids of Pending payments whose <c>ExpiresAtUtc</c> is at or before <paramref name="nowUtc"/>, oldest first.</summary>
    Task<IReadOnlyList<Guid>> GetOverduePendingIdsAsync(DateTime nowUtc, int max, CancellationToken ct);

    void Add(Payment payment);

    /// <summary>Saves all changes, plus the outbox rows published by domain-event handlers, in one transaction.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}
