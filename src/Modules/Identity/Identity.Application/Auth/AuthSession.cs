using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Auth;

internal static class AuthSession
{
    /// <summary>Builds the response for a user whose refresh token has just been created.</summary>
    public static AuthResult Build(User user, ITokenIssuer tokens, string rawRefresh, RefreshToken refresh, DateTime nowUtc)
    {
        var access = tokens.Issue(user, nowUtc);
        return new AuthResult(new AuthResponse(access.Value, access.ExpiresAtUtc, UserDto.From(user)), rawRefresh, refresh.ExpiresAtUtc);
    }
}
