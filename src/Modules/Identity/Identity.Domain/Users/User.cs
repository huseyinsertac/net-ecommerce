using Shop.SharedKernel;

namespace Identity.Domain.Users;

public sealed class User : AggregateRoot<Guid>
{
    private const int MaxDisplayNameLength = 80;
    private static readonly TimeSpan PruneAfterExpiry = TimeSpan.FromDays(1);

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
        _refreshTokens.RemoveAll(t => t.ExpiresAtUtc < nowUtc - PruneAfterExpiry);

        var token = new RefreshToken(Id, tokenHash, familyId ?? Guid.CreateVersion7(), nowUtc, lifetime);
        _refreshTokens.Add(token);
        return token;
    }

    public Result<RefreshToken> Rotate(string presentedHash, string newHash, DateTime nowUtc, TimeSpan lifetime)
    {
        var existing = _refreshTokens.FirstOrDefault(t => t.TokenHash == presentedHash);
        if (existing is null)
        {
            return IdentityErrors.InvalidRefreshToken();
        }

        if (existing.RevokedAtUtc is not null)
        {
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

    public void RevokeFamily(Guid familyId, DateTime nowUtc)
    {
        foreach (var token in _refreshTokens.Where(t => t.FamilyId == familyId))
        {
            token.Revoke(nowUtc);
        }
    }

    public void RevokeAll(DateTime nowUtc)
    {
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
