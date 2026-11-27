using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Payments.Application.Payments;
using Shop.Application.Abstractions.Identity;
using Shop.Application.Abstractions.Messaging;
using Shop.Infrastructure.Common.Http;

namespace Payments.Infrastructure.Endpoints;

/// <summary>The simulated payment page's API. Mapped only with the Fake provider; no money moves.</summary>
internal static class SimulatedPaymentEndpoints
{
    public const string DeclinedReason = "declined";

    private const string Tag = "Payments";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/simulated/{orderId:guid}", PayAsync)
            .RequireAuthorization()
            .WithName("SimulatePayment")
            .WithTags(Tag)
            .WithSummary("Reports the outcome chosen on the simulated payment page (test mode without Stripe). A payment that has already ended is left unchanged.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private sealed record SimulatedPaymentRequest(
        [property: JsonRequired, Description("True for \"Pay\", false for \"Decline\".")] bool Succeed);

    private static async Task<IResult> PayAsync(
        Guid orderId, SimulatedPaymentRequest body, ICurrentUser currentUser, IDispatcher dispatcher, CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId || userId == Guid.Empty)
        {
            return TypedResults.Unauthorized();
        }

        var session = await dispatcher.Query(new GetCustomerPaymentSession(userId, orderId), ct);
        if (session.IsFailure)
        {
            return session.Error.ToProblem();
        }

        var outcome = body.Succeed
            ? new ApplyGatewayOutcome(session.Value, GatewayOutcome.Succeeded, null)
            : new ApplyGatewayOutcome(session.Value, GatewayOutcome.Failed, DeclinedReason);
        var result = await dispatcher.Send(outcome, ct);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }
}
