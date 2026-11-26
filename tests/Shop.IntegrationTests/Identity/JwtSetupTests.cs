using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shop.Application.Abstractions.Identity;
using Shouldly;

namespace Shop.IntegrationTests.Identity;

[Collection(ShopApiCollection.Name)]
public sealed class JwtSetupTests(ShopApiFactory factory)
{
    private IServiceProvider Services
    {
        get
        {
            using var _ = factory.CreateClient();
            return factory.Services;
        }
    }

    [Fact]
    public async Task IssuedAccessToken_ValidatesAgainstConfiguredParameters()
    {
        var now = DateTime.UtcNow;
        var user = User.CreateDemoAdmin(Email.Create($"admin-{Guid.NewGuid():N}@x.test").Value, now);
        var issuer = Services.GetRequiredService<ITokenIssuer>();

        var token = issuer.Issue(user, now);

        token.ExpiresAtUtc.ShouldBe(now.AddMinutes(15), TimeSpan.FromSeconds(1));

        // 1) Independently of the host: the fixed test key and the agreed issuer and audience.
        var independent = new TokenValidationParameters
        {
            ValidIssuer = "voltlane",
            ValidAudience = "voltlane-web",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ShopApiFactory.TestSigningKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        };
        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token.Value, independent);
        result.IsValid.ShouldBeTrue(result.Exception?.Message);

        var jwt = (JsonWebToken)result.SecurityToken;
        jwt.Issuer.ShouldBe("voltlane");
        jwt.Audiences.ShouldBe(["voltlane-web"]);
        jwt.Subject.ShouldBe(user.Id.ToString());
        jwt.GetClaim("email").Value.ShouldBe(user.Email.Value);
        jwt.GetClaim("name").Value.ShouldBe("Demo Admin");
        jwt.GetClaim("demo").Value.ShouldBe("true");
        jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value)
            .ShouldBe([Roles.Admin, Roles.Customer], ignoreOrder: true);
        jwt.ValidTo.ShouldBe(now.AddMinutes(15), TimeSpan.FromSeconds(1));

        // 2) The host's own JwtBearer configuration accepts it and maps role/name claims.
        var bearer = Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        bearer.MapInboundClaims.ShouldBeFalse();
        var parameters = bearer.TokenValidationParameters;
        parameters.ValidateIssuer.ShouldBeTrue();
        parameters.ValidateAudience.ShouldBeTrue();
        parameters.ValidateLifetime.ShouldBeTrue();
        parameters.ValidateIssuerSigningKey.ShouldBeTrue();
        parameters.ClockSkew.ShouldBe(TimeSpan.FromSeconds(30));

        var hostResult = await handler.ValidateTokenAsync(token.Value, parameters);
        hostResult.IsValid.ShouldBeTrue(hostResult.Exception?.Message);
        var principal = new ClaimsPrincipal(hostResult.ClaimsIdentity);
        principal.IsInRole(Roles.Admin).ShouldBeTrue();
        principal.Identity!.Name.ShouldBe("Demo Admin");

        // 3) A token signed with another key is rejected.
        var forged = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "voltlane",
            Audience = "voltlane-web",
            Claims = new Dictionary<string, object> { ["sub"] = user.Id.ToString() },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('x', 48))), SecurityAlgorithms.HmacSha256),
        });
        (await handler.ValidateTokenAsync(forged, parameters)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void DemoFlag_IsFalse_ForRegisteredUser()
    {
        var now = DateTime.UtcNow;
        var user = User.Register(Email.Create($"u-{Guid.NewGuid():N}@x.test").Value, "Ada", "hash", now).Value;

        var token = Services.GetRequiredService<ITokenIssuer>().Issue(user, now);

        var jwt = new JsonWebToken(token.Value);
        jwt.GetClaim("demo").Value.ShouldBe("false");
        jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).ShouldBe([Roles.Customer]);
    }

    [Fact]
    public void RefreshTokenFactory_Creates32ByteBase64UrlToken_AndSha256Hash()
    {
        var tokens = Services.GetRequiredService<IRefreshTokenFactory>();

        var (raw, hash) = tokens.Create();

        Base64Url.DecodeFromChars(raw).Length.ShouldBe(32);
        raw.ShouldNotContain("=");
        hash.ShouldBe(tokens.Hash(raw));
        hash.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw))));
        tokens.Create().Raw.ShouldNotBe(raw);
    }

    [Fact]
    public void CurrentUser_ReadsSubAndRoleClaims()
    {
        var userId = Guid.NewGuid();
        using var scope = Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var current = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        try
        {
            accessor.HttpContext = new DefaultHttpContext();
            current.IsAuthenticated.ShouldBeFalse();
            current.UserId.ShouldBeNull();
            current.IsInRole(Roles.Customer).ShouldBeFalse();

            accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("sub", userId.ToString()), new Claim("role", Roles.Customer)],
                    authenticationType: "Bearer", nameType: "name", roleType: "role")),
            };
            current.IsAuthenticated.ShouldBeTrue();
            current.UserId.ShouldBe(userId);
            current.IsInRole(Roles.Customer).ShouldBeTrue();
            current.IsInRole(Roles.Admin).ShouldBeFalse();
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    [Fact]
    public async Task Production_WithShortSigningKey_FailsStartup()
    {
        await using var shortKey = new SigningKeyFactory("short");
        await shortKey.InitializeAsync();

        var ex = Should.Throw<Exception>(() => shortKey.CreateClient().Dispose());

        var validation = Unwrap<OptionsValidationException>(ex);
        validation.ShouldNotBeNull($"Expected OptionsValidationException, got {ex}");
        validation.Message.ShouldContain("Jwt:SigningKey");
        validation.Message.ShouldNotContain("short");
    }

    [Fact]
    public async Task Production_WithMissingSigningKey_FailsStartup()
    {
        await using var missing = new SigningKeyFactory("");
        await missing.InitializeAsync();

        var ex = Should.Throw<Exception>(() => missing.CreateClient().Dispose());

        Unwrap<OptionsValidationException>(ex).ShouldNotBeNull($"Expected OptionsValidationException, got {ex}");
    }

    private static T? Unwrap<T>(Exception? ex) where T : Exception
    {
        while (ex is not null)
        {
            if (ex is T match)
            {
                return match;
            }

            ex = ex is AggregateException { InnerExceptions.Count: 1 } agg ? agg.InnerExceptions[0] : ex.InnerException;
        }

        return null;
    }

    private sealed class SigningKeyFactory(string signingKey) : ShopApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Jwt:SigningKey", signingKey);
        }
    }
}
