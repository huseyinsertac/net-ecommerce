using System.ComponentModel;
using Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Ordering.Application.Admin;
using Shop.Application.Abstractions.Messaging;
using Shop.Application.Abstractions.Paging;
using Shop.Infrastructure.Common.Http;

namespace Ordering.Infrastructure.Endpoints;

/// <summary>
/// Order management for the (shared demo) admin. Every route needs the Admin policy. The only actions are ship
/// (Paid to Shipped) and deliver (Shipped to Delivered); anything else is 409 <c>ordering.invalid_transition</c>.
/// </summary>
internal static class OrderingAdminEndpoints
{
    private const string Tag = "Ordering admin";

    public static void Map(RouteGroupBuilder group)
    {
        group.RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapGet("/orders", ListAsync)
            .WithName("ListAdminOrders")
            .WithSummary("Every customer's orders, newest first. Filter by status, or search by order number (or prefix) or customer email.")
            .Produces<PagedResult<AdminOrderSummaryDto>>()
            .ProducesValidationProblem()
            .WithAdminDefaults();

        group.MapGet("/orders/{id:guid}", GetAsync)
            .WithName("GetAdminOrder")
            .WithSummary("Any customer's order with its lines, address, totals, history, owner and whether a refund is owed.")
            .Produces<AdminOrderDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithAdminDefaults();

        group.MapPost("/orders/{id:guid}/ship", ShipAsync)
            .WithName("ShipAdminOrder")
            .WithSummary("Marks a Paid order as Shipped. Any other state is a 409.")
            .Produces<AdminOrderDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithAdminDefaults();

        group.MapPost("/orders/{id:guid}/deliver", DeliverAsync)
            .WithName("DeliverAdminOrder")
            .WithSummary("Marks a Shipped order as Delivered. Any other state is a 409.")
            .Produces<AdminOrderDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithAdminDefaults();
    }

    private static RouteHandlerBuilder WithAdminDefaults(this RouteHandlerBuilder builder) => builder
        .WithTags(Tag)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> ListAsync(
        [FromQuery(Name = "status"), Description("Placed, Paid, Shipped, Delivered or Cancelled (any case).")] string? status,
        [FromQuery(Name = "q"), Description("An order number or prefix (VL-1234, or just digits), or part of a customer email.")] string? q,
        [FromQuery(Name = "page"), Description("1-based page number. Defaults to 1.")] int? page,
        [FromQuery(Name = "pageSize"), Description("Page size, 1 to 50. Defaults to 20.")] int? pageSize,
        IDispatcher dispatcher,
        CancellationToken ct)
    {
        var result = await dispatcher.Query(
            new ListAdminOrders(status, q, page ?? 1, pageSize ?? ListAdminOrders.DefaultPageSize), ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> GetAsync(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.Query(new GetAdminOrder(id), ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> ShipAsync(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.Send(new ShipOrder(id), ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> DeliverAsync(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.Send(new DeliverOrder(id), ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}
