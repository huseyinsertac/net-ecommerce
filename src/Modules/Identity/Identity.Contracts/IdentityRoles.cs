namespace Identity.Contracts;

/// <summary>The role names carried in the access token's <c>role</c> claims, for other modules' authorization checks.</summary>
public static class IdentityRoles
{
    public const string Admin = "Admin";
    public const string Customer = "Customer";
}
