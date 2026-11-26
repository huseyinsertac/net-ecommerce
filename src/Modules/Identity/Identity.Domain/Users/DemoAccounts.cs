namespace Identity.Domain.Users;

/// <summary>The fixed identities behind the one-click demo sign-ins.</summary>
public static class DemoAccounts
{
    public const string EmailDomain = "demo.voltlane.test";

    /// <summary>The single shared demo admin, created by the identity seeder.</summary>
    public const string AdminEmail = "admin@" + EmailDomain;

    /// <summary>A fresh address for a new throwaway demo customer: <c>demo-&lt;8 hex&gt;@demo.voltlane.test</c>.</summary>
    public static string NewCustomerEmail() =>
        $"demo-{Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}@{EmailDomain}";
}
