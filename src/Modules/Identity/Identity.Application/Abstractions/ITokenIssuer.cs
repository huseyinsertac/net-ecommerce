using Identity.Domain.Users;

namespace Identity.Application.Abstractions;

/// <summary>Issues short-lived signed access tokens (JWT) for a user.</summary>
public interface ITokenIssuer
{
    AccessToken Issue(User user, DateTime nowUtc);
}

public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);
