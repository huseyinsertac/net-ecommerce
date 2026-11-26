using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Shop.Infrastructure.Common.Http;

namespace Shop.Api;

/// <summary>
/// Per-client-IP fixed-window limits on the anonymous account endpoints. Limits come from configuration
/// (<c>RateLimiting:DemoCustomerPerMinute</c>, <c>RateLimiting:AuthPerMinute</c>) so tests can lower or raise them.
/// The client address is <c>RemoteIpAddress</c>, which the forwarded-headers middleware (first in the pipeline)
/// has already replaced with the real client IP from <c>X-Forwarded-For</c>.
/// </summary>
internal static class RateLimitingConfiguration
{
    private const int DefaultDemoCustomerPerMinute = 5;
    private const int DefaultAuthPerMinute = 20;

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

            options.AddPolicy(RateLimitPolicies.DemoCustomer, context =>
                PerIp(context, RateLimitPolicies.DemoCustomer, "RateLimiting:DemoCustomerPerMinute", DefaultDemoCustomerPerMinute));
            options.AddPolicy(RateLimitPolicies.AuthStrict, context =>
                PerIp(context, RateLimitPolicies.AuthStrict, "RateLimiting:AuthPerMinute", DefaultAuthPerMinute));
        });

        return services;
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
