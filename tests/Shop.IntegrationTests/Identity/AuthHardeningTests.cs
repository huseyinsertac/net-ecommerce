using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Application.Abstractions;
using Identity.Application.Auth;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Shop.IntegrationTests.AuthTestClient;

namespace Shop.IntegrationTests.Identity;

[Collection(ShopApiCollection.Name)]
public sealed class AuthHardeningTests(ShopApiFactory factory)
{
    private static readonly DateTime NowUtc = DateTime.UtcNow;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Services
    {
        get
        {
            using var _ = factory.CreateClient();
            return factory.Services;
        }
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    [Fact]
    public async Task ConcurrentLogoutAndRefresh_NeverLeavesALiveTokenAfterLogout()
    {
        for (var i = 0; i < 8; i++)
        {
            var client = factory.CreateClient();
            var first = RefreshValue(await RegisterAsync(client, NewEmail()))!;

            var results = await Task.WhenAll(
                PostWithRefreshAsync(factory.CreateClient(), "/api/identity/logout", first),
                PostWithRefreshAsync(factory.CreateClient(), "/api/identity/refresh", first));

            results[0].StatusCode.ShouldBe(HttpStatusCode.NoContent);
            results[1].StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Unauthorized);

            // Whatever the interleaving, the family is dead afterwards: the original and any token the refresh issued.
            (await PostWithRefreshAsync(client, "/api/identity/refresh", first)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            if (results[1].StatusCode == HttpStatusCode.OK)
            {
                var issued = RefreshValue(results[1])!;
                (await PostWithRefreshAsync(client, "/api/identity/refresh", issued)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }
        }
    }

    [Fact]
    public async Task StaleCopyOfUser_AfterAnotherContextRevokedTheFamily_SaveConflicts()
    {
        var user = User.Register(Email.Create($"v-{Guid.NewGuid():N}@x.test").Value, "V", "hash", NowUtc).Value;
        var token = user.IssueRefreshToken("h-" + Guid.NewGuid().ToString("N"), NowUtc, TimeSpan.FromDays(7));
        await using (var seed = Services.CreateAsyncScope())
        {
            var repo = seed.ServiceProvider.GetRequiredService<IUserRepository>();
            repo.Add(user);
            await repo.SaveChangesAsync(Ct);
        }

        await using var scopeA = Services.CreateAsyncScope();
        await using var scopeB = Services.CreateAsyncScope();
        var repoA = scopeA.ServiceProvider.GetRequiredService<IUserRepository>();
        var repoB = scopeB.ServiceProvider.GetRequiredService<IUserRepository>();
        var a = (await repoA.GetByRefreshTokenHashAsync(token.TokenHash, Ct))!;
        var b = (await repoB.GetByRefreshTokenHashAsync(token.TokenHash, Ct))!;

        // A logs out (revokes the family); B, working from its stale copy, rotates. B must not silently win.
        a.RevokeFamily(token.FamilyId, NowUtc.AddSeconds(1));
        await repoA.SaveChangesAsync(Ct);
        b.Rotate(token.TokenHash, "h2-" + Guid.NewGuid().ToString("N"), NowUtc.AddSeconds(1), TimeSpan.FromDays(7));

        await Should.ThrowAsync<ConcurrencyConflictException>(() => repoB.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Login_DemoUser_GetsTheSameProblemAsWrongPassword()
    {
        var email = Email.Create($"demo-{Guid.NewGuid():N}@demo.voltlane.test").Value;
        await using (var scope = Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            repo.Add(User.CreateDemoCustomer(email, NowUtc));
            await repo.SaveChangesAsync(Ct);
        }

        var client = factory.CreateClient();
        var registered = NewEmail();
        (await RegisterAsync(client, registered)).EnsureSuccessStatusCode();

        var demo = await LoginAsync(client, email.Value);
        var wrong = await LoginAsync(client, registered, "Wrong-pass1");

        demo.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var d = await JsonAsync(demo);
        var w = await JsonAsync(wrong);
        d.GetProperty("type").GetString().ShouldBe("identity.invalid_credentials");
        d.GetProperty("type").GetString().ShouldBe(w.GetProperty("type").GetString());
        d.GetProperty("title").GetString().ShouldBe(w.GetProperty("title").GetString());
        RefreshSetCookie(demo).ShouldBeNull();
    }

    [Fact]
    public async Task Me_ForDeletedUser_401WithStableCode()
    {
        var client = factory.CreateClient();
        var register = await RegisterAsync(client, NewEmail());
        var id = (await JsonAsync(register)).GetProperty("user").GetProperty("id").GetGuid();
        Bearer(client, await AccessTokenAsync(register));

        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync(Ct);
        }

        var me = await client.GetAsync("/api/identity/me", Ct);

        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await JsonAsync(me)).GetProperty("type").GetString().ShouldBe("identity.user_not_found");
    }

    [Fact]
    public void SecretsAreRedactedFromToString()
    {
        const string password = "Sup3r-secret-pw";
        const string token = "raw-refresh-token-value";
        var user = new UserDto(Guid.NewGuid(), "a@b.test", "A", ["Customer"], false);
        var result = new AuthResult(new AuthResponse("jwt-access-value", DateTime.UtcNow, user), token, DateTime.UtcNow);

        string[] texts =
        [
            new Login("a@b.test", password).ToString(),
            new Register("a@b.test", password, "A").ToString(),
            new Refresh(token).ToString(),
            new Logout(token).ToString(),
            result.ToString(),
            result.Response.ToString(),
        ];

        foreach (var text in texts)
        {
            text.ShouldNotContain(password);
            text.ShouldNotContain(token);
            text.ShouldNotContain("jwt-access-value");
        }

        texts[0].ShouldContain("Password = ***");
    }
}
