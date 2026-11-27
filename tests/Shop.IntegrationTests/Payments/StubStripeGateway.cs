using System.Collections.Concurrent;
using Payments.Application.Gateways;

namespace Shop.IntegrationTests.Payments;

/// <summary>Stands in for Stripe in <see cref="StripeModeApiFactory"/>: no network, session states set by the test.</summary>
public sealed class StubStripeGateway : IPaymentGateway
{
    public ConcurrentDictionary<string, GatewaySessionState> States { get; } = new();

    public ConcurrentBag<string> ExpiredSessions { get; } = [];

    public string Name => "Stripe";

    public Task<GatewaySession> CreateSessionAsync(GatewaySessionRequest request, CancellationToken ct) =>
        Task.FromResult(new GatewaySession($"cs_test_{request.OrderId:N}", $"https://checkout.stripe.test/{request.OrderId:N}"));

    public Task<GatewaySessionState> GetSessionStateAsync(string sessionId, CancellationToken ct) =>
        Task.FromResult(States.GetValueOrDefault(sessionId, GatewaySessionState.Open));

    public Task ExpireSessionAsync(string sessionId, CancellationToken ct)
    {
        ExpiredSessions.Add(sessionId);
        States[sessionId] = GatewaySessionState.Expired;
        return Task.CompletedTask;
    }
}
