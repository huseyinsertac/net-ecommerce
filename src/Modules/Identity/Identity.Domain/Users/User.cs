using Shop.SharedKernel;

namespace Identity.Domain.Users;

public sealed class User : AggregateRoot<Guid>
{
    private const int MaxDisplayNameLength = 80;
    private static readonly TimeSpan PruneAfterExpiry = TimeSpan.FromDays(1);

    /// <summary>
    /// How long after a rotation the old token may still be presented without it counting as theft, so that two browser
    /// tabs refreshing together do not log each other out.
    /// </summary>
    public static readonly TimeSpan ReuseGrace = TimeSpan.FromSeconds(30);

    private List<string> _roles = [];
    private List<RefreshToken> _refreshTokens = [];

    private User() { }

    public Email Email { get; private set; } = null!;
    public string DisplayName { get; private set; } = string.Empty;
    public string? PasswordHash { get; private set; }
    public bool IsDemo { get; private set; }
    public IReadOnlyList<string> Roles => _roles;
    public DateTime CreatedAtUtc { get; private set; }
    public IReadOnlyList<RefreshToken> RefreshTokens => _refreshTokens;

    /// <summary>
    /// Bumped by every refresh-token mutation. Token rows have no concurrency token of their own, so this makes any two
    /// concurrent token writes on the same user conflict on the user row (xmin) instead of silently interleaving.
    /// </summary>
    public int SessionVersion { get; private set; }

    public bool CanLogInWithPassword => PasswordHash is not null && !IsDemo;

    public static Result<User> Register(Email email, string displayName, string passwordHash, DateTime nowUtc)
    {
        var name = displayName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxDisplayNameLength)
        {
            return IdentityErrors.InvalidDisplayName();
        }

        return Build(email, name, passwordHash, false, [Identity.Domain.Users.Roles.Customer], nowUtc);
    }

    public static User CreateDemoCustomer(Email email, DateTime nowUtc) =>
        Build(email, "Demo Customer", null, true, [Identity.Domain.Users.Roles.Customer], nowUtc);

    public static User CreateDemoAdmin(Email email, DateTime nowUtc) =>
        Build(email, "Demo Admin", null, true,
            [Identity.Domain.Users.Roles.Admin, Identity.Domain.Users.Roles.Customer], nowUtc);

    public RefreshToken IssueRefreshToken(string tokenHash, DateTime nowUtc, TimeSpan lifetime, Guid? familyId = null)
    {
        SessionVersion++;
        _refreshTokens.RemoveAll(t => t.ExpiresAtUtc < nowUtc - PruneAfterExpiry);

        var token = new RefreshToken(Id, tokenHash, familyId ?? Guid.CreateVersion7(), nowUtc, lifetime);
        _refreshTokens.Add(token);
        return token;
    }

    public Result<RefreshToken> Rotate(string presentedHash, string newHash, DateTime nowUtc, TimeSpan lifetime,
        TimeSpan? reuseGrace = null)
    {
        var existing = _refreshTokens.FirstOrDefault(t => t.TokenHash == presentedHash);
        if (existing is null)
        {
            return IdentityErrors.InvalidRefreshToken();
        }

        if (existing.RevokedAtUtc is not null)
        {
            if (IsBenignConcurrentReuse(existing, nowUtc, reuseGrace ?? ReuseGrace))
            {
                existing.MarkGraceUsed(nowUtc);
                return IssueRefreshToken(newHash, nowUtc, lifetime, existing.FamilyId);
            }

            RevokeFamily(existing.FamilyId, nowUtc);
            return IdentityErrors.InvalidRefreshToken();
        }

        if (!existing.IsActive(nowUtc))
        {
            return IdentityErrors.InvalidRefreshToken();
        }

        var replacement = IssueRefreshToken(newHash, nowUtc, lifetime, existing.FamilyId);
        existing.Revoke(nowUtc, replacement.Id);
        return replacement;
    }

    // Rotated (has a replacement), only just now, not expired, and the family was not killed by a logout or an earlier
    // reuse detection (a killed family has a revoked token with no replacement).
    private bool IsBenignConcurrentReuse(RefreshToken existing, DateTime nowUtc, TimeSpan grace) =>
        grace > TimeSpan.Zero
        && existing.ReplacedById is not null
        && existing.GraceUsedAtUtc is null
        && nowUtc - existing.RevokedAtUtc!.Value <= grace
        && nowUtc < existing.ExpiresAtUtc
        && !_refreshTokens.Any(t => t.FamilyId == existing.FamilyId && t.RevokedAtUtc is not null && t.ReplacedById is null);

    public void RevokeFamily(Guid familyId, DateTime nowUtc)
    {
        SessionVersion++;
        foreach (var token in _refreshTokens.Where(t => t.FamilyId == familyId))
        {
            token.Revoke(nowUtc);
        }
    }

    public void RevokeAll(DateTime nowUtc)
    {
        SessionVersion++;
        foreach (var token in _refreshTokens)
        {
            token.Revoke(nowUtc);
        }
    }

    private static User Build(Email email, string displayName, string? passwordHash, bool isDemo,
        List<string> roles, DateTime nowUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        Email = email,
        DisplayName = displayName,
        PasswordHash = passwordHash,
        IsDemo = isDemo,
        _roles = roles,
        CreatedAtUtc = nowUtc
    };
}
