namespace Shop.Infrastructure.Common.Http;

/// <summary>Names of the rate-limit policies modules attach with <c>RequireRateLimiting</c>; the host defines them.</summary>
public static class RateLimitPolicies
{
    /// <summary>Anonymous creation of throwaway demo customers (account spam).</summary>
    public const string DemoCustomer = "demo-customer";

    /// <summary>Register, login and refresh share one budget per client IP (credential stuffing).</summary>
    public const string AuthStrict = "auth-strict";
}
