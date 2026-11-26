using Identity.Domain.Users;

namespace Identity.Application.Auth;

public sealed record UserDto(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles, bool IsDemo)
{
    internal static UserDto From(User user) =>
        new(user.Id, user.Email.Value, user.DisplayName, [.. user.Roles], user.IsDemo);
}

public sealed record AuthResponse(string AccessToken, DateTime ExpiresAtUtc, UserDto User)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("AccessToken = ***, ExpiresAtUtc = ").Append(ExpiresAtUtc.ToString("O")).Append(", User = ").Append(User);
        return true;
    }
}

/// <summary>Application-to-endpoint hand-off. The raw refresh token is for the cookie only and never goes in a body.</summary>
public sealed record AuthResult(AuthResponse Response, string RefreshTokenRaw, DateTime RefreshExpiresAtUtc)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("RefreshTokenRaw = ***, RefreshExpiresAtUtc = ").Append(RefreshExpiresAtUtc.ToString("O"));
        return true;
    }
}
