using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
/// not rate-limited, and exempt from the CSRF header (<see cref="CsrfHeader.ExemptPaths"/>). Because anyone can call it,
/// the body is capped at <see cref="MaxBodyBytes"/> (413 beyond that) before the signature is checked over the raw body;
/// each event id is recorded in the same transaction as the payment change, so a redelivery is a no-op.
/// </summary>
internal static class StripeWebhookEndpoint
{
    public const string Path = "/stripe/webhook";

    public const string SignatureHeader = "Stripe-Signature";

    public const string AsyncPaymentFailedReason = "async_payment_failed";

    /// <summary>Checkout Session events are a few KB; anything far larger is not from Stripe.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(Path, HandleAsync)
            .AllowAnonymous()
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes)) // enforced by the server where it supports a limit
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

        if (await ReadBodyAsync(request, ct) is not { } body)
        {
            logger.LogWarning("Rejected a Stripe webhook: body over {MaxBodyBytes} bytes.", MaxBodyBytes);
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

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

    /// <summary>
    /// The body as text, or null when it is over <see cref="MaxBodyBytes"/>: refused up front from the Content-Length,
    /// and otherwise counted while reading (a chunked body has no length up front).
    /// </summary>
    private static async Task<string?> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8 * 1024];
        try
        {
            int read;
            while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > MaxBodyBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return null; // the server's own limit (from the metadata above) tripped first
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
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
