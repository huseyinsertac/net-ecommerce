using Identity.Domain.Users;

namespace Identity.Application.Abstractions;

/// <summary>Loads and saves the <see cref="User"/> aggregate.</summary>
public interface IUserRepository
{
    /// <summary>The user with all of its refresh tokens (tracked, for writes).</summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>The user without any refresh tokens, untracked. For reads only: never mutate or save it.</summary>
    Task<User?> GetByIdWithoutTokensAsync(Guid id, CancellationToken ct);

    /// <summary>Looks a user up by <see cref="Email.Normalized"/> (upper-invariant), with all of its refresh tokens.</summary>
    Task<User?> GetByNormalizedEmailAsync(string normalized, CancellationToken ct);

    /// <summary>
    /// Finds the user owning a refresh token with this hash, whether or not the token is still active. Only that token's
    /// family is loaded into <see cref="User.RefreshTokens"/>, which is all rotation, the reuse grace and
    /// <see cref="User.RevokeFamily"/> need. Do not call <see cref="User.RevokeAll"/> on it; use <see cref="GetByIdAsync"/>.
    /// </summary>
    Task<User?> GetByRefreshTokenHashAsync(string tokenHash, CancellationToken ct);

    void Add(User user);

    Task SaveChangesAsync(CancellationToken ct);
}
