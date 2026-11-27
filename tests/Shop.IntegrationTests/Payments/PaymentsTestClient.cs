using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payments.Domain.Payments;
using Payments.Infrastructure.Persistence;
using Shop.Infrastructure.Common.Outbox;
using Shop.IntegrationTests.Ordering;
using Shop.SharedKernel;
using Shouldly;
using static Shop.IntegrationTests.Ordering.OrderingTestClient;

namespace Shop.IntegrationTests.Payments;

/// <summary>Helpers for the payments tests: orders placed end to end, payment rows, and the payments outbox.</summary>
public static class PaymentsTestClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// A signed-in shopper places a real order (one Silver laptop, 1099.99). The outbox then carries it through Catalog
    /// (stock reserved) to Payments, which opens the payment.
    /// </summary>
    public static async Task<(Shopper Shopper, Guid OrderId)> PlaceOrderAsync(ShopApiFactory factory, CancellationToken ct)
    {
        using (factory.CreateClient())
        {
            // forces host start (and startup migration)
        }

        var laptop = await SeedLaptopAsync(factory.Services, ct);
        var shopper = await SignInAsync(factory, ct);
        await AddToBasketAsync(shopper.Client, Silver(laptop), 1, ct);
        var (orderId, _) = await PlaceAsync(shopper.Client, 1099.99m, ct);
        return (shopper, orderId);
    }

    public static async Task<Payment?> FindAsync(IServiceProvider services, Guid orderId, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        return await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.OrderId == orderId, ct);
    }

    public static async Task<int> CountAsync(IServiceProvider services, Guid orderId, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        return await db.Payments.CountAsync(p => p.OrderId == orderId, ct);
    }

    /// <summary>Polls until the order's payment exists and satisfies <paramref name="condition"/> (default: exists).</summary>
    public static async Task<Payment> WaitForPaymentAsync(
        IServiceProvider services, Guid orderId, CancellationToken ct, Func<Payment, bool>? condition = null,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        Payment? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last = await FindAsync(services, orderId, ct);
            if (last is not null && (condition?.Invoke(last) ?? true))
            {
                return last;
            }

            await Task.Delay(100, ct);
        }

        throw new ShouldAssertException(
            $"Payment for order {orderId} did not reach the expected state in time (last: {last?.Status.ToString() ?? "none"}).");
    }

    /// <summary>Every <typeparamref name="T"/> event in the payments outbox (processed or not).</summary>
    public static async Task<IReadOnlyList<T>> OutboxEventsAsync<T>(IServiceProvider services, CancellationToken ct)
        where T : IIntegrationEvent
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var rows = await db.Set<OutboxMessage>().AsNoTracking().Where(m => m.Type == typeof(T).FullName).ToListAsync(ct);
        return [.. rows.Select(r => JsonSerializer.Deserialize<T>(r.Content, Json)!)];
    }

    /// <summary>Adds a Pending payment directly, for tests that do not need an order behind it.</summary>
    public static async Task<Payment> SeedPendingAsync(
        IServiceProvider services, string provider, string sessionId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var payment = Payment.Start(
            Guid.NewGuid(), Guid.NewGuid(), Money.Usd(49.99m), provider, sessionId,
            $"https://checkout.test/{sessionId}", now, now.AddMinutes(30));
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);
        return payment;
    }
}
