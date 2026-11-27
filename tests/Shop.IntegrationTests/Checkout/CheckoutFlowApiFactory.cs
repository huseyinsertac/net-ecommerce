using Microsoft.AspNetCore.Hosting;

namespace Shop.IntegrationTests.Checkout;

/// <summary>
/// The real host with its outbox processors idle after their first, startup poll (one-hour polling interval), so a test
/// decides when each module's messages are delivered (<see cref="CheckoutFlowClient.DeliverAllAsync"/>). Own database
/// container. The payment timeout stays at the shared host's five minutes, so no step races the expiry job.
/// </summary>
public class CheckoutFlowApiFactory : ShopApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Outbox:PollingInterval", "01:00:00");
    }
}
