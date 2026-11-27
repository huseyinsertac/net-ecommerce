using Microsoft.AspNetCore.Hosting;

namespace Shop.IntegrationTests.Payments;

/// <summary>
/// The shared host with a 2-second payment timeout, so an unpaid payment expires during a test. Own database container;
/// kept apart from the shared factory so the happy-path tests never race the expiry job.
/// </summary>
public sealed class PaymentExpiryApiFactory : ShopApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Payments:Timeout", "00:00:02");
    }
}
