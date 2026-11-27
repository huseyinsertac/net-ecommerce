using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Shop.Infrastructure.Common.Http;

/// <summary>
/// Every state-changing <c>/api/</c> request must carry <c>X-Requested-With: voltlane</c>. A plain HTML form or a
/// no-preflight <c>fetch</c> from another (even same-site) origin cannot set a custom header, and the API has no CORS
/// policy, so a cross-origin preflight for one is refused by the browser. This backs up the SameSite cookies while the
/// site lives on a shared domain (see ADR-0007).
/// </summary>
public static class CsrfHeader
{
    public const string Name = "X-Requested-With";
    public const string Value = "voltlane";
    public const string ErrorCode = "request.missing_csrf_header";

    /// <summary>
    /// Exact paths (case-insensitive) that are never checked: server-to-server callbacks that cannot send the header and
    /// authenticate themselves instead. The Stripe webhook verifies Stripe's signature over the raw body.
    /// </summary>
    public static IReadOnlyList<string> ExemptPaths { get; } = ["/api/payments/stripe/webhook"];

    /// <summary>
    /// True for the requests the check applies to: any method but GET, HEAD and OPTIONS, under <c>/api/</c>, except
    /// <see cref="ExemptPaths"/>.
    /// </summary>
    public static bool AppliesTo(HttpRequest request) =>
        !HttpMethods.IsGet(request.Method)
        && !HttpMethods.IsHead(request.Method)
        && !HttpMethods.IsOptions(request.Method)
        && request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        && !IsExempt(request.Path);

    private static bool IsExempt(PathString path)
    {
        var value = path.Value?.TrimEnd('/') ?? "";
        foreach (var exempt in ExemptPaths)
        {
            if (string.Equals(value, exempt, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Answers 400 ProblemDetails <c>request.missing_csrf_header</c> before routing, rate limiting or auth.</summary>
    public static IApplicationBuilder UseCsrfHeaderCheck(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var request = context.Request;
        if (AppliesTo(request)
            && !string.Equals(request.Headers[Name].ToString(), Value, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Type = ErrorCode,
                    Title = $"Missing the {Name} header.",
                    Status = StatusCodes.Status400BadRequest,
                },
                options: null,
                contentType: "application/problem+json",
                cancellationToken: context.RequestAborted);
            return;
        }

        await next(context);
    });
}
