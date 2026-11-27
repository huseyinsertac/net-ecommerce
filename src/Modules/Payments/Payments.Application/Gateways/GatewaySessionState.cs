namespace Payments.Application.Gateways;

public enum GatewaySessionState
{
    Open,
    Paid,
    Expired,

    /// <summary>
    /// The shopper finished the session but the money has not landed yet (an asynchronous method); its result arrives
    /// later as a notification. The session can no longer be closed.
    /// </summary>
    Processing,
}
