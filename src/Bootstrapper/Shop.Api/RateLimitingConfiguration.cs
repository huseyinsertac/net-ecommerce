using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Identity.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Shop.Infrastructure.Common.Http;

namespace Shop.Api;

/// <summary>
/// Per-client-IP fixed-window limits on the account endpoints, basket writes and checkout. Limits come from configuration so
/// tests can lower or raise them: <c>RateLimiting:DemoCustomerPerMinute</c> (5), <c>RateLimiting:AuthPerMinute</c> (20),
/// <c>RateLimiting:RefreshPerMinute</c> (120; only calls that carry the refresh cookie count),
/// <c>RateLimiting:BasketWritesPerMinute</c> (60) and <c>RateLimiting:CheckoutPerMinute</c> (10). On top of those, a global
/// per-IP limit covers every <c>/api</c> request: <c>RateLimiting:ApiPerMinute</c> (300). It skips the Stripe
/// webhook (Stripe's servers share a few IPs and must never be throttled by shopper traffic) and everything outside
/// <c>/api</c> (the SPA's static files and <c>/health</c>). The client address is
/// <c>RemoteIpAddress</c>, which the forwarded-headers middleware (first in the pipeline) has already replaced with the real
/// client IP from <c>X-Forwarded-For</c>.
/// </summary>
internal static class RateLimitingConfiguration
{
    private const int DefaultDemoCustomerPerMinute = 5;
    private const int DefaultAuthPerMinute = 20;
    private const int DefaultRefreshPerMinute = 120;
    private const int DefaultBasketWritesPerMinute = 60;
    private const int DefaultCheckoutPerMinute = 10;
    private const int DefaultApiPerMinute = 300;
    private const string StripeWebhookPath = "/api/payments/stripe/webhook";

    public static IServiceCollection AddShopRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await response.WriteAsJsonAsync(
                    new ProblemDetails
                    {
                        Type = "rate_limited",
                        Title = "Too many requests. Please wait a minute.",
                        Status = StatusCodes.Status429TooManyRequests,
                    },
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken: ct);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(GlobalPartition);

            options.AddPolicy(RateLimitPolicies.DemoCustomer, context =>
                PerIp(context, RateLimitPolicies.DemoCustomer, "RateLimiting:DemoCustomerPerMinute", DefaultDemoCustomerPerMinute));
            options.AddPolicy(RateLimitPolicies.AuthStrict, context =>
                PerIp(context, RateLimitPolicies.AuthStrict, "RateLimiting:AuthPerMinute", DefaultAuthPerMinute));
            // A call without the refresh cookie is answered 204 straight away (no session to restore), so every anonymous
            // page load does not eat into the budget of signed-in users behind the same IP.
            options.AddPolicy(RateLimitPolicies.AuthRefresh, context =>
                context.Request.Cookies.ContainsKey(IdentityModule.RefreshCookieName)
                    ? PerIp(context, RateLimitPolicies.AuthRefresh, "RateLimiting:RefreshPerMinute", DefaultRefreshPerMinute)
                    : RateLimitPartition.GetNoLimiter($"{RateLimitPolicies.AuthRefresh}:no-cookie"));
            options.AddPolicy(RateLimitPolicies.BasketWrite, context =>
                PerIp(context, RateLimitPolicies.BasketWrite, "RateLimiting:BasketWritesPerMinute", DefaultBasketWritesPerMinute));
            options.AddPolicy(RateLimitPolicies.Checkout, context =>
                PerIp(context, RateLimitPolicies.Checkout, "RateLimiting:CheckoutPerMinute", DefaultCheckoutPerMinute));
        });

        return services;
    }

    private static RateLimitPartition<string> GlobalPartition(HttpContext context)
    {
        var path = context.Request.Path;
        var excluded = !path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || path.Value!.TrimEnd('/').Equals(StripeWebhookPath, StringComparison.OrdinalIgnoreCase);
        return excluded
            ? RateLimitPartition.GetNoLimiter("global:excluded")
            : PerIp(context, "global", "RateLimiting:ApiPerMinute", DefaultApiPerMinute);
    }

    private static RateLimitPartition<string> PerIp(HttpContext context, string policy, string configKey, int fallback)
    {
        var limit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue(configKey, fallback);
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"{policy}:{ip}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    }
}
