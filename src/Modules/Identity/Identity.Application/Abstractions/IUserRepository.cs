using Identity.Domain.Users;

namespace Identity.Application.Abstractions;

/// <summary>Loads and saves the <see cref="User"/> aggregate, always with its refresh tokens.</summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Looks a user up by <see cref="Email.Normalized"/> (upper-invariant).</summary>
    Task<User?> GetByNormalizedEmailAsync(string normalized, CancellationToken ct);

    /// <summary>Finds the user owning a refresh token with this hash, whether or not the token is still active.</summary>
    Task<User?> GetByRefreshTokenHashAsync(string tokenHash, CancellationToken ct);

    void Add(User user);

    Task SaveChangesAsync(CancellationToken ct);
}
