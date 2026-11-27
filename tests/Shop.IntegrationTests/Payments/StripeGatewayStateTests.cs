using Payments.Application.Gateways;
using Payments.Infrastructure.Gateways;
using Shouldly;

namespace Shop.IntegrationTests.Payments;

/// <summary>The pure mapping from a Stripe Checkout Session to <see cref="GatewaySessionState"/>; no host, no HTTP.</summary>
public sealed class StripeGatewayStateTests
{
    [Theory]
    [InlineData("open", "unpaid", GatewaySessionState.Open)]
    [InlineData("complete", "paid", GatewaySessionState.Paid)]
    [InlineData("complete", "unpaid", GatewaySessionState.Processing)]
    [InlineData("expired", "unpaid", GatewaySessionState.Expired)]
    public void Maps_session_status_and_payment_status(string status, string paymentStatus, GatewaySessionState expected) =>
        StripeGateway.ToState(status, paymentStatus).ShouldBe(expected);
}
