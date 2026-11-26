using Basket.Application.Baskets;
using Basket.Domain.Baskets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shop.Application.Abstractions.Identity;
using Shop.Application.Abstractions.Messaging;
using Shop.Infrastructure.Common.Http;

namespace Basket.Infrastructure.Endpoints;

/// <summary>
/// The shopper's basket, anonymous (the <c>vl_basket</c> cookie) or signed in (the bearer token). Every read joins live
/// catalog prices and stock. A bearer that is present but invalid or expired gets 401 rather than silently falling back
/// to the anonymous basket, so the client refreshes and retries.
/// </summary>
internal static class BasketEndpoints
{
    private const string Tag = "Basket";

    public static void Map(RouteGroupBuilder group)
    {
        group.AddEndpointFilter(RejectInvalidBearerAsync);

        group.MapGet("/", GetAsync)
            .WithName("GetBasket")
            .WithTags(Tag)
            .WithSummary("The current basket with live prices and stock. Empty when there is no basket.")
            .Produces<BasketDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPut("/items/{variantId:guid}", SetQuantityAsync)
            .WithName("SetBasketItemQuantity")
            .WithTags(Tag)
            .WithSummary("Sets a line's quantity (0 removes it). The first anonymous write issues the vl_basket cookie.")
            .Produces<BasketDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/items/{variantId:guid}", RemoveAsync)
            .WithName("RemoveBasketItem")
            .WithTags(Tag)
            .WithSummary("Removes a line. Removing a line that is not there succeeds.")
            .Produces<BasketDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/", ClearAsync)
            .WithName("ClearBasket")
            .WithTags(Tag)
            .WithSummary("Empties the basket.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/merge", MergeAsync)
            .RequireAuthorization()
            .WithName("MergeBasket")
            .WithTags(Tag)
            .WithSummary("After sign-in: merges the vl_basket cookie's basket into the user's, deletes it and expires the cookie. Repeating it changes nothing.")
            .Produces<BasketDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private sealed record SetQuantityRequest(int Quantity);

    private static async Task<IResult> GetAsync(HttpContext http, ICurrentUser currentUser, IDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.Query(new GetBasket(BasketCookie.ResolveOwner(http, currentUser)), ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> SetQuantityAsync(
        Guid variantId, SetQuantityRequest body, HttpContext http, ICurrentUser currentUser, IDispatcher dispatcher,
        CancellationToken ct)
    {
        var owner = BasketCookie.ResolveOwner(http, currentUser);
        var minted = false;
        if (owner is null)
        {
            if (currentUser.IsAuthenticated)
            {
                return TypedResults.Unauthorized(); // a principal without a usable subject must not get an anonymous basket
            }

            owner = BasketOwner.ForAnonymous(BasketCookie.NewAnonymousId());
            minted = true;
        }

        var result = await dispatcher.Send(new SetItemQuantity(owner, variantId, body.Quantity), ct);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        // Re-issued on every successful anonymous write so the 30 days slide; never for a signed-in caller.
        if (owner.AnonymousId is { } anonymousId && (!minted || result.Value.Lines.Count > 0))
        {
            BasketCookie.Append(http.Response, anonymousId);
        }

        return TypedResults.Ok(result.Value);
    }

    private static async Task<IResult> RemoveAsync(
        Guid variantId, HttpContext http, ICurrentUser currentUser, IDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.Send(new RemoveItem(BasketCookie.ResolveOwner(http, currentUser), variantId), ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> ClearAsync(HttpContext http, ICurrentUser currentUser, IDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.Send(new ClearBasket(BasketCookie.ResolveOwner(http, currentUser)), ct);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    private static async Task<IResult> MergeAsync(HttpContext http, ICurrentUser currentUser, IDispatcher dispatcher, CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        var result = await dispatcher.Send(new MergeBaskets(userId, BasketCookie.ReadAnonymousId(http.Request)), ct);
        if (result.IsFailure)
        {
            return result.Error.ToProblem(); // the cookie stays, so the client can simply retry
        }

        if (BasketCookie.IsPresent(http.Request))
        {
            BasketCookie.Clear(http.Response);
        }

        return TypedResults.Ok(result.Value);
    }

    /// <summary>
    /// These routes allow anonymous callers, so the authorization middleware never rejects a bad token here. A bearer that
    /// was sent but failed validation (expired, tampered) is answered with the scheme's challenge (401) instead of being
    /// treated as "no token".
    /// </summary>
    private static async ValueTask<object?> RejectInvalidBearerAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var authentication = await context.HttpContext.AuthenticateAsync();
        return authentication.Failure is not null ? TypedResults.Challenge() : await next(context);
    }
}
