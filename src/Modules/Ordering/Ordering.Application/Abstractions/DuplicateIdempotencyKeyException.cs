namespace Ordering.Application.Abstractions;

/// <summary>
/// Thrown by <see cref="IOrderRepository.SaveChangesAsync"/> when the unique index on (customer, idempotency key) rejects
/// the order because a concurrent request with the same key saved first. The repository has already discarded its tracked
/// state, so a reload in the same scope reads the winner.
/// </summary>
public sealed class DuplicateIdempotencyKeyException(Exception inner)
    : Exception("An order with this idempotency key already exists.", inner);
