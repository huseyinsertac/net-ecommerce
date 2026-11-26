using System.Net;
using System.Text.Json;
using Shouldly;
using static Shop.IntegrationTests.AuthTestClient;

namespace Shop.IntegrationTests.Identity;

/// <summary>Strict reuse detection: the grace is 0 here, so any replay of a rotated token counts as theft.</summary>
public sealed class RefreshReuseDetectionTests(ZeroGraceApiFactory factory) : IClassFixture<ZeroGraceApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Refresh_RotatesCookie_OldCookieThenRejected()
    {
        var client = factory.CreateClient();
        var register = await RegisterAsync(client, NewEmail());
        var first = RefreshValue(register)!;

        var refreshed = await PostWithRefreshAsync(client, "/api/identity/refresh", first);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = RefreshValue(refreshed)!;
        second.ShouldNotBeNullOrEmpty();
        second.ShouldNotBe(first);
        (await AccessTokenAsync(refreshed)).ShouldNotBeNullOrWhiteSpace();

        var old = await PostWithRefreshAsync(client, "/api/identity/refresh", first);
        old.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await JsonAsync(old)).GetProperty("type").GetString().ShouldBe("identity.invalid_refresh_token");
    }

    [Fact]
    public async Task Refresh_ReplayOfRotatedToken_RevokesFamily_NewTokenAlsoDead()
    {
        var client = factory.CreateClient();
        var first = RefreshValue(await RegisterAsync(client, NewEmail()))!;
        var second = RefreshValue(await PostWithRefreshAsync(client, "/api/identity/refresh", first))!;

        // The attacker replays the already-rotated token ...
        var replay = await PostWithRefreshAsync(client, "/api/identity/refresh", first);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // ... and the victim's newest token dies with it.
        var victim = await PostWithRefreshAsync(client, "/api/identity/refresh", second);
        victim.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await JsonAsync(victim)).GetProperty("type").GetString().ShouldBe("identity.invalid_refresh_token");
    }
}
