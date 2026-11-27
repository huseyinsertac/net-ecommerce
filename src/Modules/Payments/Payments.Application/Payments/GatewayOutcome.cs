namespace Payments.Application.Payments;

/// <summary>How a payment session ended, as reported by the gateway (webhook, simulated page or expiry check).</summary>
public enum GatewayOutcome
{
    Succeeded,
    Failed,
    Expired,
}
