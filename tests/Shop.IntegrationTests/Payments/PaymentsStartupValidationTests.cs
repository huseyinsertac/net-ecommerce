using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Shop.IntegrationTests.Payments;

/// <summary>Production refuses any payment configuration that could charge a real card or skip Stripe.</summary>
public sealed class PaymentsStartupValidationTests
{
    /// <summary>Shaped like a live key, but not a real one.</summary>
    private const string LiveShapedKey = "sk_live_NOT-A-REAL-KEY-0000";

    [Fact]
    public async Task Production_with_live_key_fails_startup()
    {
        var validation = await StartupFailureAsync(new()
        {
            ["Payments:Provider"] = "Stripe",
            ["Payments:Timeout"] = "00:30:00",
            ["Stripe:SecretKey"] = LiveShapedKey,
            ["Stripe:WebhookSecret"] = "whsec_test",
        });

        validation.Message.ShouldContain("Stripe:SecretKey");
        validation.Message.ShouldNotContain(LiveShapedKey);
    }

    [Fact]
    public async Task Production_with_fake_provider_fails_startup()
    {
        var validation = await StartupFailureAsync(new()
        {
            ["Payments:Provider"] = "Fake",
            ["Payments:AllowFakeProviderInProduction"] = "false",
        });

        validation.Message.ShouldContain("Payments:Provider");
    }

    [Fact]
    public async Task Stripe_with_bad_webhook_secret_or_short_timeout_fails_startup()
    {
        var validation = await StartupFailureAsync(new()
        {
            ["Payments:Provider"] = "Stripe",
            ["Payments:Timeout"] = "00:05:00",
            ["Stripe:SecretKey"] = "sk_test_dummy",
            ["Stripe:WebhookSecret"] = "not-a-webhook-secret",
        });

        validation.Message.ShouldContain("Stripe:WebhookSecret");
        validation.Message.ShouldContain("Payments:Timeout");
        validation.Message.ShouldNotContain("not-a-webhook-secret");
    }

    private static async Task<OptionsValidationException> StartupFailureAsync(Dictionary<string, string> settings)
    {
        await using var factory = new SettingsFactory(settings);
        await factory.InitializeAsync();

        var ex = Should.Throw<Exception>(() => factory.CreateClient().Dispose());

        var validation = Unwrap<OptionsValidationException>(ex);
        validation.ShouldNotBeNull($"Expected OptionsValidationException, got {ex}");
        return validation;
    }

    private static T? Unwrap<T>(Exception? ex) where T : Exception
    {
        while (ex is not null)
        {
            if (ex is T match)
            {
                return match;
            }

            ex = ex is AggregateException { InnerExceptions.Count: 1 } agg ? agg.InnerExceptions[0] : ex.InnerException;
        }

        return null;
    }

    private sealed class SettingsFactory(Dictionary<string, string> settings) : ShopApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }
}
