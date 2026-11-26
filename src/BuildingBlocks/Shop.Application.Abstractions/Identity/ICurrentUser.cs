namespace Shop.Application.Abstractions.Identity;

/// <summary>The caller of the current request, as established by authentication. Anonymous when no valid token was sent.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The <c>sub</c> claim, or null when anonymous or when the claim is not a GUID.</summary>
    Guid? UserId { get; }

    /// <summary>The <c>email</c> claim, or null when anonymous or when the token carries none.</summary>
    string? Email { get; }

    bool IsInRole(string role);

    /// <summary>True when the access token carries <c>demo</c> = <c>"true"</c> (a demo customer or the shared demo admin).</summary>
    bool IsDemo { get; }
}
