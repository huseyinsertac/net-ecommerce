using Identity.Application.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shop.Application.Abstractions.Identity;
using Shop.Application.Abstractions.Messaging;
using Shop.Infrastructure.Common.Http;
using Shop.SharedKernel;

namespace Identity.Infrastructure.Endpoints;

/// <summary>Registration, sign-in, refresh-token rotation, sign-out and the current user.</summary>
internal static class IdentityEndpoints
{
    private const string Tag = "Identity";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/register", RegisterAsync)
            .WithName("RegisterUser")
            .WithTags(Tag)
            .WithSummary("Creates an account and signs it in. The refresh token is set as an HttpOnly cookie.")
            .Produces<AuthResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/login", LoginAsync)
            .WithName("LoginUser")
            .WithTags(Tag)
            .WithSummary("Signs in with email and password.")
            .Produces<AuthResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", RefreshAsync)
            .WithName("RefreshSession")
            .WithTags(Tag)
            .WithSummary("Rotates the refresh cookie and returns a new access token. Replaying a rotated token revokes its family.")
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .WithName("LogoutUser")
            .WithTags(Tag)
            .WithSummary("Revokes the refresh token family and clears the cookie. Always succeeds.")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", MeAsync)
            .RequireAuthorization()
            .WithName("GetCurrentUser")
            .WithTags(Tag)
            .WithSummary("The signed-in user.")
            .Produces<UserDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private sealed record RegisterRequest(string Email, string Password, string DisplayName);

    private sealed record LoginRequest(string Email, string Password);

    private static async Task<IResult> RegisterAsync(RegisterRequest body, IDispatcher dispatcher, HttpResponse response, CancellationToken ct)
    {
        var result = await dispatcher.Send(new Register(body.Email, body.Password, body.DisplayName), ct);
        return Respond(result, response, StatusCodes.Status201Created);
    }

    private static async Task<IResult> LoginAsync(LoginRequest body, IDispatcher dispatcher, HttpResponse response, CancellationToken ct)
    {
        var result = await dispatcher.Send(new Login(body.Email, body.Password), ct);
        return Respond(result, response, StatusCodes.Status200OK);
    }

    private static async Task<IResult> RefreshAsync(HttpRequest request, HttpResponse response, IDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.Send(new Refresh(RefreshCookie.Read(request)), ct);
        if (result.IsFailure)
        {
            RefreshCookie.Clear(response);
        }

        return Respond(result, response, StatusCodes.Status200OK);
    }

    private static async Task<IResult> LogoutAsync(HttpRequest request, HttpResponse response, IDispatcher dispatcher, CancellationToken ct)
    {
        await dispatcher.Send(new Logout(RefreshCookie.Read(request)), ct);
        RefreshCookie.Clear(response);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> MeAsync(ICurrentUser currentUser, IDispatcher dispatcher, CancellationToken ct)
    {
        if (currentUser.UserId is not { } id)
        {
            return TypedResults.Unauthorized();
        }

        var result = await dispatcher.Query(new GetMe(id), ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <summary>On success sets the refresh cookie and returns the body (never the raw token); otherwise a problem.</summary>
    private static IResult Respond(Result<AuthResult> result, HttpResponse response, int successStatus)
    {
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        RefreshCookie.Append(response, result.Value.RefreshTokenRaw, result.Value.RefreshExpiresAtUtc);
        return Results.Json(result.Value.Response, statusCode: successStatus);
    }
}
