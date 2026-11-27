namespace Payments.Infrastructure.Gateways;

/// <summary>
/// The <c>Stripe</c> section. Supplied only through the environment (<c>Stripe__SecretKey</c>, <c>Stripe__WebhookSecret</c>)
/// and never committed. Only test-mode keys are accepted (see <see cref="PaymentsOptionsValidation"/>).
/// </summary>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>Test-mode secret (<c>sk_test_</c>) and restricted (<c>rk_test_</c>) keys; live keys are refused.</summary>
    public static readonly IReadOnlyList<string> TestKeyPrefixes = ["sk_test_", "rk_test_"];

    public static bool IsTestModeKey(string key) =>
        TestKeyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal) && key.Length > prefix.Length);

    public const string WebhookSecretPrefix = "whsec_";

    public string SecretKey { get; set; } = "";

    public string WebhookSecret { get; set; } = "";
}
