namespace Identity.Application.Abstractions;

/// <summary>Thrown by <see cref="IUserRepository.SaveChangesAsync"/> when the email's unique index rejects the write.</summary>
public sealed class DuplicateEmailException(Exception inner) : Exception("The email is already registered.", inner);

/// <summary>
/// Thrown by <see cref="IUserRepository.SaveChangesAsync"/> when another request changed the same rows first. The repository
/// has already discarded its tracked state, so callers reload the user and retry.
/// </summary>
public sealed class ConcurrencyConflictException(Exception inner) : Exception("The user was modified concurrently.", inner);
