namespace Basket.Application.Abstractions;

/// <summary>
/// Thrown by <see cref="IBasketRepository.SaveChangesAsync"/> when another request changed (or created) the same basket
/// first. The repository has already discarded its tracked state, so a reload in the same scope reads the current rows.
/// </summary>
public sealed class BasketConcurrencyException(Exception inner) : Exception("The basket was modified concurrently.", inner);
