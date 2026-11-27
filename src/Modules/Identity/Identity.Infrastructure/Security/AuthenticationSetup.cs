using System.Text;
using Identity.Contracts;
using Identity.Domain.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.Security;

public static class AuthenticationSetup
{
    public const string RoleClaimType = "role";

    public const string NameClaimType = "name";

    /// <summary>
    /// JWT bearer authentication for the whole host plus the <see cref="AuthorizationPolicies.Admin"/> authorization policy.
    /// <see cref="JwtOptions"/> is validated on start, so a missing or short signing key stops the host before it
    /// serves a request (the message never echoes the key).
    /// </summary>
    public static IServiceCollection AddShopAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey ?? string.Empty) >= JwtOptions.MinimumSigningKeyBytes,
                $"Jwt:SigningKey must be at least {JwtOptions.MinimumSigningKeyBytes} bytes (UTF-8). " +
                "In production supply it through the Jwt__SigningKey environment variable.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience),
                "Jwt:Issuer and Jwt:Audience must be set.")
            .Validate(o => o.AccessTokenMinutes > 0 && o.RefreshTokenDays > 0 && o.RefreshReuseGraceSeconds >= 0,
                "Jwt:AccessTokenMinutes and Jwt:RefreshTokenDays must be positive, Jwt:RefreshReuseGraceSeconds non-negative.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = CreateSigningKey(jwt),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RoleClaimType = RoleClaimType,
                    NameClaimType = NameClaimType,
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Admin, policy => policy.RequireRole(Roles.Admin));

        return services;
    }

    internal static SymmetricSecurityKey CreateSigningKey(JwtOptions options) =>
        new(Encoding.UTF8.GetBytes(options.SigningKey));
}
