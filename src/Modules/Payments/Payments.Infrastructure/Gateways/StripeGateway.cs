using Microsoft.Extensions.Logging;
using Payments.Application.Gateways;
using Stripe;
using Stripe.Checkout;

namespace Payments.Infrastructure.Gateways;

/// <summary>
/// Stripe Checkout in <c>payment</c> mode (test-mode keys only; see <see cref="PaymentsOptionsValidation"/>). Sessions are
/// created with the idempotency key <c>checkout-session-{orderId}</c>, so a retried handler gets the session it already
/// created instead of a second one.
/// </summary>
internal sealed class StripeGateway(IStripeClient client, ILogger<StripeGateway> logger) : IPaymentGateway
{
    /// <summary>
    /// Stripe's <c>expires_at</c> is the local deadline plus a minute (Stripe refuses anything under 30 minutes from ITS
    /// clock), rounded up to this step. Rounding keeps a retry a few seconds later byte-identical, which Stripe requires
    /// for a reused idempotency key. The session may outlive the payment by a few minutes; the expiry job closes it.
    /// </summary>
    private static readonly TimeSpan ExpiresAtStep = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan ExpiresAtMargin = TimeSpan.FromMinutes(1);

    public string Name => PaymentProviders.Stripe;

    public async Task<GatewaySession> CreateSessionAsync(GatewaySessionRequest request, CancellationToken ct)
    {
        var options = new SessionCreateOptions
        {
            Mode = "payment",
            CustomerEmail = request.CustomerEmail,
            ClientReferenceId = request.OrderId.ToString(),
            Metadata = new Dictionary<string, string>
            {
                ["order_id"] = request.OrderId.ToString(),
                ["order_number"] = request.OrderNumber,
            },
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            ExpiresAt = RoundUp(request.ExpiresAtUtc + ExpiresAtMargin),
            LineItems =
            [
                .. request.Lines.Select(line => new SessionLineItemOptions
                {
                    Quantity = line.Quantity,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = request.Currency.ToLowerInvariant(),
                        UnitAmount = ToCents(line.UnitPrice),
                        ProductData = new SessionLineItemPriceDataProductDataOptions { Name = line.Name },
                    },
                }),
            ],
        };

        var service = new SessionService(client);
        var key = $"checkout-session-{request.OrderId}";
        Session session;
        try
        {
            session = await service.CreateAsync(options, new RequestOptions { IdempotencyKey = key }, ct);
        }
        catch (StripeException ex) when (ex.StripeError?.Type == "idempotency_error")
        {
            // An earlier attempt used the key with different parameters (its expires_at fell in another step). Its session
            // was never saved, so nobody holds its URL; it simply expires. Open a fresh one.
            logger.LogWarning("Stripe refused the reused idempotency key for order {OrderId}; opening a new session.", request.OrderId);
            session = await service.CreateAsync(
                options, new RequestOptions { IdempotencyKey = $"{key}-{options.ExpiresAt:yyyyMMddHHmm}" }, ct);
        }

        return new GatewaySession(session.Id, session.Url);
    }

    public async Task<GatewaySessionState> GetSessionStateAsync(string sessionId, CancellationToken ct)
    {
        var session = await new SessionService(client).GetAsync(sessionId, cancellationToken: ct);
        return session switch
        {
            { Status: "expired" } => GatewaySessionState.Expired,
            { PaymentStatus: "paid" } => GatewaySessionState.Paid,
            _ => GatewaySessionState.Open,
        };
    }

    public Task ExpireSessionAsync(string sessionId, CancellationToken ct) =>
        new SessionService(client).ExpireAsync(sessionId, cancellationToken: ct);

    /// <summary>Stripe amounts are integers in the currency's minor unit (cents for USD).</summary>
    internal static long ToCents(decimal amount) => (long)Math.Round(amount * 100m);

    private static DateTime RoundUp(DateTime utc)
    {
        var ticks = (utc.Ticks + ExpiresAtStep.Ticks - 1) / ExpiresAtStep.Ticks * ExpiresAtStep.Ticks;
        return new DateTime(ticks, DateTimeKind.Utc);
    }
}
