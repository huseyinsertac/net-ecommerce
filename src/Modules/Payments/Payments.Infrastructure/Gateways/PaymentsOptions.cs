namespace Payments.Infrastructure.Gateways;

/// <summary>The <c>Payments</c> section, plus <c>App:PublicBaseUrl</c>. Validated on start by <see cref="PaymentsOptionsValidation"/>.</summary>
public sealed class PaymentsOptions
{
    public const string SectionName = "Payments";

    public const string PublicBaseUrlKey = "App:PublicBaseUrl";

    /// <summary><see cref="PaymentProviders.Stripe"/> or <see cref="PaymentProviders.Fake"/>.</summary>
    public string Provider { get; set; } = PaymentProviders.Fake;

    /// <summary>How long a shopper has to pay. Stripe needs 30 minutes to 23 hours.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How often the expiry job looks for Pending payments past their timeout.</summary>
    public TimeSpan ExpiryScanInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Lets the Fake provider run in Production. Only for hosts that never face real shoppers: the test host and the
    /// local Docker Compose stack. The deployed site leaves it false, so it must use Stripe (in test mode).
    /// </summary>
    public bool AllowFakeProviderInProduction { get; set; }

    /// <summary>From <c>App:PublicBaseUrl</c>, without a trailing slash: the origin return URLs are built from.</summary>
    public string PublicBaseUrl { get; set; } = "";
}
