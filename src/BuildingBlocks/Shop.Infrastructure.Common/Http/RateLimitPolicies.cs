namespace Shop.Infrastructure.Common.Http;

/// <summary>Names of the rate-limit policies modules attach with <c>RequireRateLimiting</c>; the host defines them.</summary>
public static class RateLimitPolicies
{
    /// <summary>Anonymous creation of throwaway demo customers (account spam).</summary>
    public const string DemoCustomer = "demo-customer";

    /// <summary>Register and login share one budget per client IP (credential stuffing).</summary>
    public const string AuthStrict = "auth-strict";

    /// <summary>Refresh has its own, larger per-IP budget: legitimate SPAs refresh often, and several users may share an IP.</summary>
    public const string AuthRefresh = "auth-refresh";

    /// <summary>Basket writes (PUT, DELETE, merge) per client IP: each one reads the catalog and writes a row.</summary>
    public const string BasketWrite = "basket-write";
}
