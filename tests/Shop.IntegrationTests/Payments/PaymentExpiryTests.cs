using Payments.Contracts.Events;
using Payments.Domain.Payments;
using Shouldly;
using static Shop.IntegrationTests.Payments.PaymentsTestClient;

namespace Shop.IntegrationTests.Payments;

/// <summary>The expiry job with the Fake provider and a 2-second timeout (own host, see <see cref="PaymentExpiryApiFactory"/>).</summary>
public sealed class PaymentExpiryTests(PaymentExpiryApiFactory factory) : IClassFixture<PaymentExpiryApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pending_payment_past_timeout_expires_and_publishes_PaymentExpired()
    {
        var (shopper, orderId) = await PlaceOrderAsync(factory, Ct);
        using var _ = shopper;

        var expired = await WaitForPaymentAsync(
            factory.Services, orderId, Ct, p => p.Status != PaymentStatus.Pending, TimeSpan.FromSeconds(20));

        expired.Status.ShouldBe(PaymentStatus.Expired);
        expired.CompletedAtUtc.ShouldNotBeNull();
        expired.CompletedAtUtc.Value.ShouldBeGreaterThanOrEqualTo(expired.ExpiresAtUtc);
        (await OutboxEventsAsync<PaymentExpired>(factory.Services, Ct))
            .Where(e => e.OrderId == orderId).ShouldHaveSingleItem().PaymentId.ShouldBe(expired.Id);
    }
}
