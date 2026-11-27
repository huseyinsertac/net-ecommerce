namespace Payments.Application.Gateways;

/// <summary>A payment provider that hosts checkout sessions (Stripe, or the simulated Fake gateway).</summary>
public interface IPaymentGateway
{
    /// <summary>Stored on each payment as <c>Provider</c>: <c>Stripe</c> or <c>Fake</c>.</summary>
    string Name { get; }

    /// <summary>Opens a session. Retrying for the same order returns the same session (idempotent per order).</summary>
    Task<GatewaySession> CreateSessionAsync(GatewaySessionRequest request, CancellationToken ct);

    Task<GatewaySessionState> GetSessionStateAsync(string sessionId, CancellationToken ct);

    /// <summary>Closes an open session so it can no longer be paid.</summary>
    Task ExpireSessionAsync(string sessionId, CancellationToken ct);
}
