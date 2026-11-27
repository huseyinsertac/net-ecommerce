using System.Text.Json;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Infrastructure.Persistence;
using Payments.Infrastructure.Persistence;
using Shop.Infrastructure.Common.Outbox;
using Shouldly;
using static Shop.IntegrationTests.Ordering.OrderingTestClient;

namespace Shop.IntegrationTests.Checkout;

/// <summary>Drives the checkout choreography by hand and observes it through the checkout-status endpoint.</summary>
public static class CheckoutFlowClient
{
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Delivers every pending message of Ordering, Catalog and Payments, in that order, round after round, until a whole
    /// round finds nothing to deliver.
    /// </summary>
    public static async Task DeliverAllAsync(IServiceProvider services, CancellationToken ct)
    {
        var ordering = services.GetRequiredService<OutboxProcessor<OrderingDbContext>>();
        var catalog = services.GetRequiredService<OutboxProcessor<CatalogDbContext>>();
        var payments = services.GetRequiredService<OutboxProcessor<PaymentsDbContext>>();
        int delivered;
        do
        {
            delivered = await ordering.ProcessBatchAsync(ct)
                        + await catalog.ProcessBatchAsync(ct)
                        + await payments.ProcessBatchAsync(ct);
        }
        while (delivered > 0);
    }

    public static async Task<JsonElement> GetStatusAsync(HttpClient client, Guid orderId, CancellationToken ct) =>
        await JsonAsync(await client.GetAsync($"/api/ordering/orders/{orderId}/checkout-status", ct), ct);

    /// <summary>
    /// Delivers pending messages and reads the checkout status until it reports <paramref name="state"/>; fails after
    /// 10 seconds with the last status seen.
    /// </summary>
    public static async Task<JsonElement> WaitForStateAsync(
        IServiceProvider services, HttpClient client, Guid orderId, string state, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + StatusTimeout;
        JsonElement last;
        do
        {
            await DeliverAllAsync(services, ct);
            last = await GetStatusAsync(client, orderId, ct);
            if (last.GetProperty("state").GetString() == state)
            {
                return last;
            }

            await Task.Delay(100, ct);
        }
        while (DateTime.UtcNow < deadline);

        throw new ShouldAssertException($"Checkout status of order {orderId} never became {state}; last: {last.GetRawText()}");
    }

    /// <summary>The status carries no payment link (the property is absent or null).</summary>
    public static void NoPaymentUrl(JsonElement status) =>
        (!status.TryGetProperty("paymentUrl", out var url) || url.ValueKind == JsonValueKind.Null)
            .ShouldBeTrue($"Expected no paymentUrl: {status.GetRawText()}");

    public static async Task<ProductVariant> VariantAsync(IServiceProvider services, Guid productId, Guid variantId, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var product = await db.Products.AsNoTracking().Include(p => p.Variants).SingleAsync(p => p.Id == productId, ct);
        return product.Variants.Single(v => v.Id == variantId);
    }

    /// <summary>Holds <paramref name="quantity"/> units straight through Catalog, as another shopper's reservation would.</summary>
    public static async Task ReserveDirectlyAsync(
        IServiceProvider services, Guid productId, Guid variantId, int quantity, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var product = await db.Products.Include(p => p.Variants).SingleAsync(p => p.Id == productId, ct);
        product.ReserveStock(variantId, quantity).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Moves the order's payment deadline into the past, as if the shopper had walked away for the whole timeout. Used
    /// instead of a host with a 2-second timeout, so this class needs one host and its other tests never race the expiry job.
    /// </summary>
    public static async Task ExpirePaymentNowAsync(IServiceProvider services, Guid orderId, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var updated = await db.Payments
            .Where(p => p.OrderId == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ExpiresAtUtc, DateTime.UtcNow.AddSeconds(-1)), ct);
        updated.ShouldBe(1);
    }

    public static async Task<string[]> HistoryAsync(HttpClient client, Guid orderId, CancellationToken ct)
    {
        var order = await JsonAsync(await client.GetAsync($"/api/ordering/orders/{orderId}", ct), ct);
        return [.. order.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("status").GetString()!)];
    }
}
