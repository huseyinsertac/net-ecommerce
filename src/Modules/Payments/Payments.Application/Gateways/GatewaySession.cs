namespace Payments.Application.Gateways;

/// <param name="Url">Where the shopper is sent to pay.</param>
public sealed record GatewaySession(string SessionId, string Url);
