using Shop.SharedKernel;

namespace Identity.Domain.Users;

public sealed class RefreshToken : Entity<Guid>
{
    private RefreshToken() { }

    internal RefreshToken(Guid userId, string tokenHash, Guid familyId, DateTime nowUtc, TimeSpan lifetime)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        CreatedAtUtc = nowUtc;
        ExpiresAtUtc = nowUtc + lifetime;
    }

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public Guid FamilyId { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public Guid? ReplacedById { get; private set; }

    /// <summary>Set when the once-only reuse grace reissue was spent on this (already rotated) token.</summary>
    public DateTime? GraceUsedAtUtc { get; private set; }

    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && nowUtc < ExpiresAtUtc;

    internal void MarkGraceUsed(DateTime nowUtc) => GraceUsedAtUtc ??= nowUtc;

    internal void Revoke(DateTime nowUtc, Guid? replacedById = null)
    {
        RevokedAtUtc ??= nowUtc;
        ReplacedById ??= replacedById;
    }
}
