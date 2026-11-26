using Microsoft.Extensions.Options;
using Payments.Application.Gateways;

namespace Payments.Infrastructure.Gateways;

/// <summary>
/// The simulated gateway: the shopper is sent to the app's own "Test payment" page, which reports the outcome through
/// <c>POST /api/payments/simulated/{orderId}</c>. It holds no state of its own (the payment row is the state), so a
/// session is always Open until the payment ends, and expiring one is a no-op.
/// </summary>
internal sealed class FakeGateway(IOptions<PaymentsOptions> options) : IPaymentGateway
{
    public string Name => PaymentProviders.Fake;

    public Task<GatewaySession> CreateSessionAsync(GatewaySessionRequest request, CancellationToken ct) =>
        Task.FromResult(new GatewaySession(
            $"fake_{request.OrderId:N}",
            $"{options.Value.PublicBaseUrl}/pay/simulated/{request.OrderId}"));

    public Task<GatewaySessionState> GetSessionStateAsync(string sessionId, CancellationToken ct) =>
        Task.FromResult(GatewaySessionState.Open);

    public Task ExpireSessionAsync(string sessionId, CancellationToken ct) => Task.CompletedTask;
}
