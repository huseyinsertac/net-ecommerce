using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using static Shop.IntegrationTests.AuthTestClient;

namespace Shop.IntegrationTests.Identity;

[Collection(ShopApiCollection.Name)]
public sealed class AuthEndpointTests(ShopApiFactory factory)
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Register_ReturnsTokens_AndSetsHttpOnlyStrictRefreshCookie()
    {
        var client = factory.CreateClient();
        var email = NewEmail();

        var response = await RegisterAsync(client, email, name: "Ada");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await JsonAsync(response);
        body.GetProperty("accessToken").GetString().ShouldNotBeNullOrWhiteSpace();
        body.GetProperty("user").GetProperty("email").GetString().ShouldBe(email);
        body.GetProperty("user").GetProperty("isDemo").GetBoolean().ShouldBeFalse();
        body.GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldBe(["Customer"]);
        body.ToString().ShouldNotContain("refresh", Case.Insensitive);

        var cookie = RefreshSetCookie(response);
        cookie.ShouldNotBeNull();
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain("path=/api/identity", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        RefreshValue(response).ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Register_DuplicateEmailDifferentCase_409EmailTaken()
    {
        var client = factory.CreateClient();
        var email = NewEmail();
        (await RegisterAsync(client, email)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await RegisterAsync(client, email.ToUpperInvariant());

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await JsonAsync(second)).GetProperty("type").GetString().ShouldBe("identity.email_taken");
    }

    [Fact]
    public async Task Register_WeakPassword_400()
    {
        var response = await RegisterAsync(factory.CreateClient(), NewEmail(), password: "password");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JsonAsync(response)).GetProperty("type").GetString().ShouldBe("identity.weak_password");
    }

    [Fact]
    public async Task Login_WrongPassword_And_UnknownEmail_SameProblem401()
    {
        var client = factory.CreateClient();
        var email = NewEmail();
        (await RegisterAsync(client, email)).EnsureSuccessStatusCode();

        var wrong = await LoginAsync(client, email, "Wrong-pass1");
        var unknown = await LoginAsync(client, NewEmail(), StrongPassword);

        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var a = await JsonAsync(wrong);
        var b = await JsonAsync(unknown);
        a.GetProperty("type").GetString().ShouldBe("identity.invalid_credentials");
        b.GetProperty("type").GetString().ShouldBe("identity.invalid_credentials");
        a.GetProperty("title").GetString().ShouldBe(b.GetProperty("title").GetString());
        RefreshSetCookie(wrong).ShouldBeNull();
    }

    [Fact]
    public async Task Login_CorrectPassword_ReturnsTokensAndCookie_EmailCaseInsensitive()
    {
        var client = factory.CreateClient();
        var email = NewEmail();
        (await RegisterAsync(client, email)).EnsureSuccessStatusCode();

        var response = await LoginAsync(client, email.ToUpperInvariant());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        RefreshValue(response).ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Me_WithBearer_ReturnsUser_WithoutBearer_401()
    {
        var (client, email, _) = await RegisterAndSignInAsync(factory);

        var me = await client.GetAsync("/api/identity/me", TestContext.Current.CancellationToken);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(me)).GetProperty("email").GetString().ShouldBe(email);

        var anonymous = await factory.CreateClient().GetAsync("/api/identity/me", TestContext.Current.CancellationToken);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

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

    [Fact]
    public async Task Refresh_Failure_ClearsCookie()
    {
        var response = await PostWithRefreshAsync(factory.CreateClient(), "/api/identity/refresh", "garbage");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var cookie = RefreshSetCookie(response);
        cookie.ShouldNotBeNull();
        RefreshValue(response).ShouldBeEmpty();
        cookie.ShouldContain("path=/api/identity", Case.Insensitive);
        cookie.ShouldContain("expires=", Case.Insensitive);
    }

    [Fact]
    public async Task Logout_RevokesRefresh_SubsequentRefresh401()
    {
        var client = factory.CreateClient();
        var token = RefreshValue(await RegisterAsync(client, NewEmail()))!;

        var logout = await PostWithRefreshAsync(client, "/api/identity/logout", token);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        RefreshValue(logout).ShouldBeEmpty();
        (await PostWithRefreshAsync(client, "/api/identity/refresh", token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithoutOrWithInvalidCookie_Still204()
    {
        var client = factory.CreateClient();

        (await PostWithRefreshAsync(client, "/api/identity/logout", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PostWithRefreshAsync(client, "/api/identity/logout", "garbage")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RefreshWithoutCookie_401_NoServerError()
    {
        var response = await PostWithRefreshAsync(factory.CreateClient(), "/api/identity/refresh", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await JsonAsync(response)).GetProperty("type").GetString().ShouldBe("identity.invalid_refresh_token");
    }

    [Fact]
    public async Task Register_InvalidInput_400Validation()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/identity/register",
            new { email = "", password = "short", displayName = "" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JsonAsync(response)).GetProperty("type").GetString().ShouldBe("validation.failed");
    }
}
