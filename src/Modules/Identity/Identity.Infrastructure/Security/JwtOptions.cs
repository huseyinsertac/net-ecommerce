namespace Identity.Infrastructure.Security;

/// <summary>Bound from the <c>Jwt</c> configuration section. The signing key never lives in committed production config.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HS256 needs a key of at least 256 bits.</summary>
    public const int MinimumSigningKeyBytes = 32;

    public string Issuer { get; set; } = "voltlane";

    public string Audience { get; set; } = "voltlane-web";

    /// <summary>Symmetric HS256 key (UTF-8). In production it comes from the <c>Jwt__SigningKey</c> environment variable.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>Seconds after a rotation during which the old refresh token is still accepted once more (concurrent tabs). 0 disables it.</summary>
    public int RefreshReuseGraceSeconds { get; set; } = 30;
}
