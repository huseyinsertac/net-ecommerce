namespace Payments.Infrastructure.Gateways;

/// <summary>Values of <c>Payments:Provider</c>; also stored on each payment as its <c>Provider</c>.</summary>
public static class PaymentProviders
{
    public const string Stripe = "Stripe";

    /// <summary>The simulated gateway: nothing leaves the process and no money moves.</summary>
    public const string Fake = "Fake";
}
