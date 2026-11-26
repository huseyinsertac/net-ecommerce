using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Ordering.Application.Checkout;
using Ordering.Application.Orders;
using Ordering.Domain.Orders;
using Shop.Application.Abstractions.Identity;
using Shop.Application.Abstractions.Messaging;
using Shop.Application.Abstractions.Paging;
using Shop.Infrastructure.Common.Http;

namespace Ordering.Infrastructure.Endpoints;

/// <summary>Checkout and the signed-in customer's order history. Every route needs a bearer token.</summary>
internal static class OrderingEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private const string Tag = "Ordering";

    public static void Map(RouteGroupBuilder group)
    {
        group.RequireAuthorization();

        group.MapPost("/checkout", CheckoutAsync)
            .WithName("Checkout")
            .WithTags(Tag)
            .WithSummary("Places an order from the basket at live prices. Repeating a request with the same Idempotency-Key returns the same order. The client then polls the Location (checkout-status).")
            .RequireRateLimiting(RateLimitPolicies.Checkout)
            .Produces<PlaceOrderResult>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/orders", GetMyOrdersAsync)
            .WithName("GetMyOrders")
            .WithTags(Tag)
            .WithSummary("The caller's orders, newest first.")
            .Produces<PagedResult<OrderSummaryDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/orders/{id:guid}", GetOrderAsync)
            .WithName("GetOrder")
            .WithTags(Tag)
            .WithSummary("One of the caller's orders with its lines, address, totals and status history.")
            .Produces<OrderDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    /// <summary>A missing expected total is a 400 binding failure, never a silent 0.</summary>
    private sealed record CheckoutRequest(
        ShippingAddressDto? ShippingAddress,
        [property: JsonRequired, Description("The total the shopper was shown, in USD. A different live total is refused with price_changed.")]
        decimal ExpectedTotal);

    private static async Task<IResult> CheckoutAsync(
        CheckoutRequest body,
        [FromHeader(Name = IdempotencyKeyHeader), Description("A GUID chosen by the client per checkout attempt; reuse it when retrying.")]
        string? idempotencyKey,
        ICurrentUser currentUser,
        IDispatcher dispatcher,
        CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId || userId == Guid.Empty)
        {
            return TypedResults.Unauthorized();
        }

        if (!Guid.TryParse(idempotencyKey, out var key) || key == Guid.Empty)
        {
            return OrderErrors.MissingIdempotencyKey().ToProblem();
        }

        // Tokens issued by this app always carry the email claim, so its absence is a server bug, not a client error.
        var email = currentUser.Email
            ?? throw new InvalidOperationException("The authenticated user's token carries no email claim.");

        var result = await dispatcher.Send(new PlaceOrder(userId, email, key, body.ShippingAddress, body.ExpectedTotal), ct);
        return result.IsSuccess
            ? TypedResults.Accepted($"/api/ordering/orders/{result.Value.OrderId}/checkout-status", result.Value)
            : result.Error.ToProblem();
    }

    private static async Task<IResult> GetMyOrdersAsync(
        [FromQuery(Name = "page"), Description("1-based page number. Defaults to 1.")] int? page,
        [FromQuery(Name = "pageSize"), Description("Page size, 1 to 50. Defaults to 10.")] int? pageSize,
        ICurrentUser currentUser,
        IDispatcher dispatcher,
        CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId || userId == Guid.Empty)
        {
            return TypedResults.Unauthorized();
        }

        var result = await dispatcher.Query(
            new GetMyOrders(userId, page ?? 1, pageSize ?? GetMyOrders.DefaultPageSize), ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> GetOrderAsync(
        Guid id, ICurrentUser currentUser, IDispatcher dispatcher, CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId || userId == Guid.Empty)
        {
            return TypedResults.Unauthorized();
        }

        var result = await dispatcher.Query(new GetOrder(userId, id), ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}
