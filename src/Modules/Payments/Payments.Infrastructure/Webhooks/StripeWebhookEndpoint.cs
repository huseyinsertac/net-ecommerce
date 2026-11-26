using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Payments.Application.Payments;
using Payments.Infrastructure.Gateways;
using Payments.Infrastructure.Persistence;
using Shop.Application.Abstractions.Messaging;
using Shop.Infrastructure.Common.Http;
using Stripe;
using Stripe.Checkout;

namespace Payments.Infrastructure.Webhooks;

/// <summary>
/// <c>POST /api/payments/stripe/webhook</c>, mapped only with the Stripe provider. Anonymous (Stripe signs instead),
/// not rate-limited, and exempt from the CSRF header (<see cref="CsrfHeader.ExemptPaths"/>). The signature is checked over
/// the raw body; each event id is recorded in the same transaction as the payment change, so a redelivery is a no-op.
/// </summary>
internal static class StripeWebhookEndpoint
{
    public const string Path = "/stripe/webhook";

    public const string SignatureHeader = "Stripe-Signature";

    public const string AsyncPaymentFailedReason = "async_payment_failed";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(Path, HandleAsync)
            .AllowAnonymous()
            .ExcludeFromDescription(); // called by Stripe, not by the SPA
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        IOptions<StripeOptions> stripe,
        PaymentsDbContext db,
        IDispatcher dispatcher,
        TimeProvider clock,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var logger = loggers.CreateLogger(typeof(StripeWebhookEndpoint).FullName!);

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(ct);

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                body, request.Headers[SignatureHeader].ToString(), stripe.Value.WebhookSecret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            logger.LogWarning("Rejected a Stripe webhook: {Reason}", ex.Message);
            return Results.BadRequest();
        }

        if (stripeEvent.Data?.Object is not Session session || ToOutcome(stripeEvent.Type, session) is not { } outcome)
        {
            logger.LogDebug("Ignoring Stripe event {EventId} of type {Type}.", stripeEvent.Id, stripeEvent.Type);
            return Results.Ok();
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (await db.ProcessedWebhookEvents.AnyAsync(e => e.EventId == stripeEvent.Id, ct))
            {
                logger.LogInformation("Stripe event {EventId} was already processed; ignoring.", stripeEvent.Id);
                return Results.Ok();
            }

            db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent
            {
                EventId = stripeEvent.Id,
                ReceivedOnUtc = clock.GetUtcNow().UtcDateTime,
            });
            var reason = outcome == GatewayOutcome.Failed ? AsyncPaymentFailedReason : null;
            var result = await dispatcher.Send(new ApplyGatewayOutcome(session.Id, outcome, reason), ct);
            if (result.IsFailure)
            {
                return result.Error.ToProblem();
            }

            await db.SaveChangesAsync(ct); // the event row, when the outcome itself changed nothing
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent delivery of the same event won the primary key; it handles the outcome.
            logger.LogInformation("Stripe event {EventId} is being processed concurrently; ignoring.", stripeEvent.Id);
        }

        return Results.Ok();
    }

    /// <summary>The outcome an event reports, or null when it reports none (ignored with 200).</summary>
    private static GatewayOutcome? ToOutcome(string type, Session session) => type switch
    {
        // An asynchronous method (e.g. a bank debit) completes the session unpaid; its result arrives as a later event.
        EventTypes.CheckoutSessionCompleted when session.PaymentStatus == "paid" => GatewayOutcome.Succeeded,
        EventTypes.CheckoutSessionAsyncPaymentSucceeded => GatewayOutcome.Succeeded,
        EventTypes.CheckoutSessionAsyncPaymentFailed => GatewayOutcome.Failed,
        EventTypes.CheckoutSessionExpired => GatewayOutcome.Expired,
        _ => null,
    };
}
