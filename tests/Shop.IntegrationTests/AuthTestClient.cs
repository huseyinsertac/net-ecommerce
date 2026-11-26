using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Shop.IntegrationTests;

/// <summary>
/// Helper for the auth endpoints. The test host runs in Production, so <c>vl_refresh</c> is <c>Secure</c> and a cookie-aware
/// HttpClient would (correctly) refuse to send it back over http://localhost. Rather than weakening the cookie, the helper
/// reads <c>Set-Cookie</c> itself and sends the <c>Cookie</c> header by hand, which also lets tests replay an old value.
/// </summary>
public static class AuthTestClient
{
    public const string RefreshCookieName = "vl_refresh";
    public const string StrongPassword = "Passw0rd-test";

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@test.voltlane";

    public static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password = StrongPassword, string name = "Test User") =>
        client.PostAsJsonAsync("/api/identity/register", new { email, password, displayName = name });

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password = StrongPassword) =>
        client.PostAsJsonAsync("/api/identity/login", new { email, password });

    /// <summary>POSTs to <paramref name="path"/> presenting <paramref name="refreshToken"/> (when not null) as the refresh cookie.</summary>
    public static Task<HttpResponseMessage> PostWithRefreshAsync(HttpClient client, string path, string? refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (refreshToken is not null)
        {
            request.Headers.Add("Cookie", $"{RefreshCookieName}={refreshToken}");
        }

        return client.SendAsync(request);
    }

    /// <summary>The raw Set-Cookie header for vl_refresh, or null.</summary>
    public static string? RefreshSetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(RefreshCookieName + "=", StringComparison.Ordinal))
            : null;

    /// <summary>The refresh token value from Set-Cookie (empty string when the cookie was cleared).</summary>
    public static string? RefreshValue(HttpResponseMessage response)
    {
        var header = RefreshSetCookie(response);
        return header is null ? null : header[(RefreshCookieName.Length + 1)..].Split(';')[0];
    }

    public static async Task<string> AccessTokenAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("accessToken").GetString()!;
    }

    public static void Bearer(HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    /// <summary>Registers a fresh user and returns a client already carrying its bearer token, plus the refresh token.</summary>
    public static async Task<(HttpClient Client, string Email, string Refresh)> RegisterAndSignInAsync(ShopApiFactory factory)
    {
        var client = factory.CreateClient();
        var email = NewEmail();
        var response = await RegisterAsync(client, email);
        response.EnsureSuccessStatusCode();
        Bearer(client, await AccessTokenAsync(response));
        return (client, email, RefreshValue(response)!);
    }
}
