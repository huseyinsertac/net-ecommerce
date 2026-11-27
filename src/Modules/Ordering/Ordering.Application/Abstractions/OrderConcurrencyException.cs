namespace Ordering.Application.Abstractions;

/// <summary>
/// Thrown by <see cref="IOrderRepository.SaveChangesAsync"/> when another request changed the order since it was loaded.
/// The repository has already discarded its tracked state, so a reload in the same scope reads the winner's version.
/// </summary>
public sealed class OrderConcurrencyException(Exception inner)
    : Exception("The order was changed by another request.", inner);
