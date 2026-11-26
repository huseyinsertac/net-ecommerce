using System.Security.Claims;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.Security;

/// <summary>HS256 access tokens carrying <c>sub</c>, <c>email</c>, <c>name</c>, <c>role</c> (0..n) and <c>demo</c>.</summary>
internal sealed class JwtTokenIssuer(IOptions<JwtOptions> options) : ITokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Issue(User user, DateTime nowUtc)
    {
        var jwt = options.Value;
        var expires = nowUtc.AddMinutes(jwt.AccessTokenMinutes);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Email, user.Email.Value),
            new(AuthenticationSetup.NameClaimType, user.DisplayName),
            new("demo", user.IsDemo ? "true" : "false"),
        ];
        claims.AddRange(user.Roles.Select(role => new Claim(AuthenticationSetup.RoleClaimType, role)));

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = nowUtc,
            NotBefore = nowUtc,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                AuthenticationSetup.CreateSigningKey(jwt), SecurityAlgorithms.HmacSha256),
        });

        return new AccessToken(token, expires);
    }
}
