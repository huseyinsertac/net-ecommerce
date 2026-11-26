using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Identity.Infrastructure.Endpoints;

/// <summary>
/// The refresh token cookie: HttpOnly, SameSite=Strict, Path=/api/identity, Secure everywhere except Development.
/// </summary>
internal static class RefreshCookie
{
    public const string Name = "vl_refresh";
    public const string Path = "/api/identity";

    public static void Append(HttpResponse response, string raw, DateTime expiresUtc) =>
        response.Cookies.Append(Name, raw, Options(response, new DateTimeOffset(DateTime.SpecifyKind(expiresUtc, DateTimeKind.Utc))));

    /// <summary>Expires the cookie with the same Path so the browser matches and removes it.</summary>
    public static void Clear(HttpResponse response) =>
        response.Cookies.Append(Name, string.Empty, Options(response, DateTimeOffset.UnixEpoch));

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static CookieOptions Options(HttpResponse response, DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = !response.HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expires,
    };
}
