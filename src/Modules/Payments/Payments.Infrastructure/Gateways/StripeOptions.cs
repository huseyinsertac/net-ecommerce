namespace Payments.Infrastructure.Gateways;

/// <summary>
/// The <c>Stripe</c> section. Supplied only through the environment (<c>Stripe__SecretKey</c>, <c>Stripe__WebhookSecret</c>)
/// and never committed. Only test-mode keys are accepted (see <see cref="PaymentsOptionsValidation"/>).
/// </summary>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    public const string TestSecretKeyPrefix = "sk_test_";

    public const string WebhookSecretPrefix = "whsec_";

    public string SecretKey { get; set; } = "";

    public string WebhookSecret { get; set; } = "";
}
