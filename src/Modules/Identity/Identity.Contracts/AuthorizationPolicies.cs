namespace Identity.Contracts;

/// <summary>Authorization policy names registered by Identity, for other modules' endpoints.</summary>
public static class AuthorizationPolicies
{
    /// <summary>Requires the <see cref="IdentityRoles.Admin"/> role. Every <c>/api/*/admin/*</c> endpoint uses it.</summary>
    public const string Admin = "Admin";
}
