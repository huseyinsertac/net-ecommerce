using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Payments.Infrastructure.Gateways;

/// <summary>
/// Startup checks that keep the demo from ever charging a real card. Production must use Stripe (unless the host opts in
/// to the Fake provider); Stripe accepts only test-mode keys (<c>sk_test_</c> secret or <c>rk_test_</c> restricted) and <c>whsec_</c> webhook secrets, in every
/// environment, and a timeout Stripe can honour (30 minutes to 23 hours). Messages name settings, never their values.
/// </summary>
internal sealed class PaymentsOptionsValidation(IHostEnvironment environment, IOptions<StripeOptions> stripeOptions)
    : IValidateOptions<PaymentsOptions>
{
    private static readonly TimeSpan StripeMinimumTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan StripeMaximumTimeout = TimeSpan.FromHours(23); // Stripe allows 24 h; StripeGateway adds a few minutes

    public ValidateOptionsResult Validate(string? name, PaymentsOptions options)
    {
        var failures = new List<string>();

        if (options.Provider is not (PaymentProviders.Stripe or PaymentProviders.Fake))
        {
            failures.Add($"Payments:Provider must be '{PaymentProviders.Stripe}' or '{PaymentProviders.Fake}'.");
        }
        else if (environment.IsProduction() && options.Provider == PaymentProviders.Fake && !options.AllowFakeProviderInProduction)
        {
            failures.Add($"Payments:Provider must be '{PaymentProviders.Stripe}' in Production.");
        }

        if (options.Timeout <= TimeSpan.Zero || options.ExpiryScanInterval <= TimeSpan.Zero)
        {
            failures.Add("Payments:Timeout and Payments:ExpiryScanInterval must be positive.");
        }

        if (!Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var baseUrl)
            || baseUrl.Scheme is not ("http" or "https"))
        {
            failures.Add($"{PaymentsOptions.PublicBaseUrlKey} must be an absolute http(s) URL, such as https://shop.example.com.");
        }

        if (options.Provider == PaymentProviders.Stripe)
        {
            var stripe = stripeOptions.Value;
            if (!StripeOptions.IsTestModeKey(stripe.SecretKey))
            {
                failures.Add(
                    $"Stripe:SecretKey must be a test-mode key (starting with '{string.Join("' or '", StripeOptions.TestKeyPrefixes)}'); " +
                    "supply it through the Stripe__SecretKey environment variable.");
            }

            if (!stripe.WebhookSecret.StartsWith(StripeOptions.WebhookSecretPrefix, StringComparison.Ordinal))
            {
                failures.Add(
                    $"Stripe:WebhookSecret must start with '{StripeOptions.WebhookSecretPrefix}'; " +
                    "supply it through the Stripe__WebhookSecret environment variable.");
            }

            if (options.Timeout < StripeMinimumTimeout || options.Timeout > StripeMaximumTimeout)
            {
                failures.Add("Payments:Timeout must be between 00:30:00 and 23:00:00 with Stripe (its session lifetime limits).");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
