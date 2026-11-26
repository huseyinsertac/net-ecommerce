using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Shop.Application.Abstractions.Identity;

namespace Shop.Infrastructure.Common.Http;

/// <summary>Reads the authenticated caller from the request's principal (<c>sub</c> and <c>role</c> claims, unmapped).</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private const string SubjectClaim = "sub";
    private const string RoleClaim = "role";

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId =>
        IsAuthenticated && Guid.TryParse(Principal!.FindFirst(SubjectClaim)?.Value, out var id) ? id : null;

    public bool IsInRole(string role) => IsAuthenticated && Principal!.HasClaim(RoleClaim, role);
}
