using Basket.Domain.Baskets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shop.Application.Abstractions.Identity;

namespace Basket.Infrastructure.Endpoints;

/// <summary>
/// The anonymous basket cookie <c>vl_basket</c> (a random GUID): HttpOnly, SameSite=Lax, Path=/api/basket, 30 days, Secure
/// everywhere except Development. A missing, malformed or unknown value simply means "no basket".
/// </summary>
internal static class BasketCookie
{
    public const string Name = "vl_basket";
    public const string Path = "/api/basket";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    /// <summary>
    /// Who the request's basket belongs to. A signed-in caller is always resolved from the token and the cookie is ignored,
    /// so a stale or someone else's cookie can never select a user's basket. Otherwise the cookie's GUID, or <c>null</c>
    /// when there is no usable cookie (or an authenticated principal carries no usable subject).
    /// </summary>
    public static BasketOwner? ResolveOwner(HttpContext context, ICurrentUser currentUser)
    {
        if (currentUser.IsAuthenticated)
        {
            return currentUser.UserId is { } userId && userId != Guid.Empty ? BasketOwner.ForUser(userId) : null;
        }

        return ReadAnonymousId(context.Request) is { } anonymousId ? BasketOwner.ForAnonymous(anonymousId) : null;
    }

    /// <summary>The cookie's GUID; <c>null</c> when absent, not a GUID, or the empty GUID. Never throws.</summary>
    public static Guid? ReadAnonymousId(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;

    public static bool IsPresent(HttpRequest request) => request.Cookies.ContainsKey(Name);

    /// <summary>A fresh, unguessable id: the cookie is the only credential for an anonymous basket.</summary>
    public static Guid NewAnonymousId() => Guid.NewGuid();

    /// <summary>Issues (or re-issues, sliding the 30 days) the cookie.</summary>
    public static void Append(HttpResponse response, Guid anonymousId)
    {
        var now = response.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        response.Cookies.Append(Name, anonymousId.ToString(), Options(response, now.Add(Lifetime)));
    }

    /// <summary>Expires the cookie with the same Path so the browser matches and removes it.</summary>
    public static void Clear(HttpResponse response) =>
        response.Cookies.Append(Name, string.Empty, Options(response, DateTimeOffset.UnixEpoch));

    private static CookieOptions Options(HttpResponse response, DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = !response.HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(),
        SameSite = SameSiteMode.Lax,
        Path = Path,
        Expires = expires,
    };
}
