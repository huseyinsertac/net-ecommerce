using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payments.Contracts.Events;
using Payments.Domain.Payments;
using Payments.Infrastructure.Persistence;
using Shouldly;
using static Shop.IntegrationTests.Payments.PaymentsTestClient;

namespace Shop.IntegrationTests.Payments;

/// <summary>
/// The Stripe webhook against a Stripe-configured host whose gateway is a stub. Payloads are signed here exactly as Stripe
/// signs them: <c>Stripe-Signature: t={unix},v1={hex HMAC-SHA256("{t}.{body}", webhook secret)}</c>.
/// </summary>
public sealed class StripeWebhookTests(StripeModeApiFactory factory) : IClassFixture<StripeModeApiFactory>
{
    private const string WebhookPath = "/api/payments/stripe/webhook";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Services
    {
        get
        {
            using var _ = factory.CreateClient(); // forces host start (and startup migration)
            return factory.Services;
        }
    }

    private static string NewEventId() => $"evt_test_{Guid.NewGuid():N}";

    private static string NewSessionId() => $"cs_test_{Guid.NewGuid():N}";

    private static string EventJson(string eventId, string type, string sessionId, string paymentStatus = "paid") =>
        JsonSerializer.Serialize(new
        {
            id = eventId,
            @object = "event",
            api_version = "2025-01-27.acacia",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            livemode = false,
            pending_webhooks = 1,
            type,
            data = new
            {
                @object = new
                {
                    id = sessionId,
                    @object = "checkout.session",
                    mode = "payment",
                    payment_status = paymentStatus,
                    status = type == "checkout.session.expired" ? "expired" : "complete",
                },
            },
        });

    private static string Sign(string body, string secret = StripeModeApiFactory.WebhookSecret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(mac)}";
    }

    private async Task<HttpResponseMessage> PostAsync(string body, string? signature, bool withCsrfHeader = true)
    {
        using var client = factory.CreateClient();
        if (!withCsrfHeader)
        {
            client.DefaultRequestHeaders.Remove(ShopApiFactory.CsrfHeaderName);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (signature is not null)
        {
            request.Headers.TryAddWithoutValidation("Stripe-Signature", signature);
        }

        return await client.SendAsync(request, Ct);
    }

    private Task<HttpResponseMessage> SendEventAsync(string eventId, string type, string sessionId, string paymentStatus = "paid")
    {
        var body = EventJson(eventId, type, sessionId, paymentStatus);
        return PostAsync(body, Sign(body));
    }

    private async Task<Payment> PaymentAsync(Guid orderId) => (await FindAsync(Services, orderId, Ct))!;

    private async Task<bool> EventRecordedAsync(string eventId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        return await db.ProcessedWebhookEvents.AnyAsync(e => e.EventId == eventId, Ct);
    }

    [Fact]
    public async Task Signed_completed_paid_event_succeeds_the_payment()
    {
        var sessionId = NewSessionId();
        var payment = await SeedPendingAsync(Services, "Stripe", sessionId, Ct);
        var eventId = NewEventId();

        var response = await SendEventAsync(eventId, "checkout.session.completed", sessionId);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await PaymentAsync(payment.OrderId)).Status.ShouldBe(PaymentStatus.Succeeded);
        (await OutboxEventsAsync<PaymentSucceeded>(Services, Ct))
            .Where(e => e.OrderId == payment.OrderId).ShouldHaveSingleItem().PaymentId.ShouldBe(payment.Id);
        (await EventRecordedAsync(eventId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Same_event_id_twice_is_a_no_op()
    {
        var first = await SeedPendingAsync(Services, "Stripe", NewSessionId(), Ct);
        var second = await SeedPendingAsync(Services, "Stripe", NewSessionId(), Ct);
        var eventId = NewEventId();

        (await SendEventAsync(eventId, "checkout.session.completed", first.ProviderSessionId)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendEventAsync(eventId, "checkout.session.completed", first.ProviderSessionId)).StatusCode.ShouldBe(HttpStatusCode.OK);
        // The id alone decides: a redelivered id is skipped even if its payload pointed elsewhere.
        (await SendEventAsync(eventId, "checkout.session.expired", second.ProviderSessionId)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await PaymentAsync(first.OrderId)).Status.ShouldBe(PaymentStatus.Succeeded);
        (await PaymentAsync(second.OrderId)).Status.ShouldBe(PaymentStatus.Pending);
        (await OutboxEventsAsync<PaymentSucceeded>(Services, Ct)).Count(e => e.OrderId == first.OrderId).ShouldBe(1);
        (await OutboxEventsAsync<PaymentExpired>(Services, Ct)).ShouldNotContain(e => e.OrderId == second.OrderId);
    }

    [Fact]
    public async Task Bad_signature_is_400_and_changes_nothing()
    {
        var payment = await SeedPendingAsync(Services, "Stripe", NewSessionId(), Ct);
        var eventId = NewEventId();
        var body = EventJson(eventId, "checkout.session.completed", payment.ProviderSessionId);

        (await PostAsync(body, Sign(body, "whsec_someone_else"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostAsync(body, signature: null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostAsync(body, "t=1,v1=deadbeef")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var signedOther = EventJson(eventId, "checkout.session.completed", NewSessionId());
        (await PostAsync(body, Sign(signedOther))).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // signature of another body

        (await PaymentAsync(payment.OrderId)).Status.ShouldBe(PaymentStatus.Pending);
        (await EventRecordedAsync(eventId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Unknown_session_is_200()
    {
        var response = await SendEventAsync(NewEventId(), "checkout.session.completed", NewSessionId());

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Unhandled_event_type_is_200_and_ignored()
    {
        var payment = await SeedPendingAsync(Services, "Stripe", NewSessionId(), Ct);

        var response = await SendEventAsync(NewEventId(), "checkout.session.created", payment.ProviderSessionId);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PaymentAsync(payment.OrderId)).Status.ShouldBe(PaymentStatus.Pending);
    }

    [Fact]
    public async Task Completed_unpaid_waits_then_async_succeeded_succeeds()
    {
        var payment = await SeedPendingAsync(Services, "Stripe", NewSessionId(), Ct);

        (await SendEventAsync(NewEventId(), "checkout.session.completed", payment.ProviderSessionId, "unpaid"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PaymentAsync(payment.OrderId)).Status.ShouldBe(PaymentStatus.Pending);

        (await SendEventAsync(NewEventId(), "checkout.session.async_payment_succeeded", payment.ProviderSessionId))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PaymentAsync(payment.OrderId)).Status.ShouldBe(PaymentStatus.Succeeded);
    }

    [Fact]
    public async Task Async_payment_failed_fails_the_payment()
    {
        var payment = await SeedPendingAsync(Services, "Stripe", NewSessionId(), Ct);

        (await SendEventAsync(NewEventId(), "checkout.session.async_payment_failed", payment.ProviderSessionId, "unpaid"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var failed = await PaymentAsync(payment.OrderId);
        failed.Status.ShouldBe(PaymentStatus.Failed);
        failed.FailureReason.ShouldBe("async_payment_failed");
        (await OutboxEventsAsync<PaymentFailed>(Services, Ct))
            .Where(e => e.OrderId == payment.OrderId).ShouldHaveSingleItem().Reason.ShouldBe("async_payment_failed");
    }

    [Fact]
    public async Task Expired_event_expires()
    {
        var payment = await SeedPendingAsync(Services, "Stripe", NewSessionId(), Ct);

        (await SendEventAsync(NewEventId(), "checkout.session.expired", payment.ProviderSessionId, "unpaid"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await PaymentAsync(payment.OrderId)).Status.ShouldBe(PaymentStatus.Expired);
        (await OutboxEventsAsync<PaymentExpired>(Services, Ct))
            .Where(e => e.OrderId == payment.OrderId).ShouldHaveSingleItem().PaymentId.ShouldBe(payment.Id);
    }

    [Fact]
    public async Task Webhook_needs_no_csrf_header()
    {
        var payment = await SeedPendingAsync(Services, "Stripe", NewSessionId(), Ct);
        var body = EventJson(NewEventId(), "checkout.session.completed", payment.ProviderSessionId);

        var response = await PostAsync(body, Sign(body), withCsrfHeader: false);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await PaymentAsync(payment.OrderId)).Status.ShouldBe(PaymentStatus.Succeeded);
    }

    [Fact]
    public async Task Simulated_payment_endpoint_is_not_mapped_with_Stripe()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/payments/simulated/{Guid.NewGuid()}", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Expiry_job_marks_paid_when_gateway_reports_paid()
    {
        var paid = await SeedPendingAsync(Services, "Stripe", NewSessionId(), Ct);
        var open = await SeedPendingAsync(Services, "Stripe", NewSessionId(), Ct);
        factory.Gateway.States[paid.ProviderSessionId] = global::Payments.Application.Gateways.GatewaySessionState.Paid;

        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            await db.Payments
                .Where(p => p.Id == paid.Id || p.Id == open.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)), Ct);
        }

        (await WaitForPaymentAsync(Services, paid.OrderId, Ct, p => p.Status != PaymentStatus.Pending))
            .Status.ShouldBe(PaymentStatus.Succeeded);
        (await WaitForPaymentAsync(Services, open.OrderId, Ct, p => p.Status != PaymentStatus.Pending))
            .Status.ShouldBe(PaymentStatus.Expired);
        factory.Gateway.ExpiredSessions.ShouldContain(open.ProviderSessionId); // an open session is closed at Stripe first
        factory.Gateway.ExpiredSessions.ShouldNotContain(paid.ProviderSessionId);
        (await OutboxEventsAsync<PaymentSucceeded>(Services, Ct)).ShouldContain(e => e.PaymentId == paid.Id);
    }
}
