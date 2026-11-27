using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Payments.Application.Gateways;

namespace Shop.IntegrationTests.Payments;

/// <summary>
/// The host configured for Stripe (dummy test-mode values only, never a real key), with the gateway replaced by
/// <see cref="StubStripeGateway"/> so nothing leaves the process. Own database container. Logs go to
/// <see cref="Logs"/> (Information and up) instead of Serilog, so tests can assert on them.
/// </summary>
public class StripeModeApiFactory : ShopApiFactory
{
    /// <summary>Deliberately NON-SECRET values that only pass the prefix checks.</summary>
    public const string SecretKey = "sk_test_dummy";

    public const string WebhookSecret = "whsec_test";

    public StubStripeGateway Gateway { get; } = new();

    public CapturedLogs Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Payments:Provider", "Stripe");
        builder.UseSetting("Payments:Timeout", "00:30:00");
        builder.UseSetting("Stripe:SecretKey", SecretKey);
        builder.UseSetting("Stripe:WebhookSecret", WebhookSecret);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(Gateway);
            // Serilog's factory ignores DI-registered providers, so the factory itself is swapped for one that captures.
            services.RemoveAll<ILoggerFactory>();
            services.AddSingleton<ILoggerFactory>(_ =>
                new LoggerFactory([Logs], new LoggerFilterOptions { MinLevel = LogLevel.Information }));
        });
    }
}
