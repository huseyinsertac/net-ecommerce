using System.Security.Cryptography;
using System.Text;
using Catalog.Contracts.Variants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Persistence;
using Shop.Infrastructure.Common.Modules;

namespace Ordering.Infrastructure.Seeding;

/// <summary>
/// Gives the admin a realistic order list: 24 orders from 8 fictional <c>@example.com</c> customers over the last 30
/// days, six each of Placed, Paid, Shipped and Delivered (oldest first, so the oldest is <c>VL-100001</c> on a fresh
/// sequence). All are historical: no OrderPlaced is published, no stock is touched and no payment exists. Does nothing
/// when any order exists. Needs the catalog seeded first; the Ordering module is registered after Catalog.
/// </summary>
internal sealed class OrderingSeeder(
    OrderingDbContext db,
    IOrderRepository orders,
    ICatalogVariantQuery catalog,
    TimeProvider clock,
    ILogger<OrderingSeeder> logger) : IModuleSeeder
{
    private const int SampleSize = 8;
    private const int OrdersPerStatus = 6;

    private sealed record Customer(string Name, string Email, string Line1, string City, string PostalCode, string Country);

    private static readonly Customer[] Customers =
    [
        new("Alex Morgan", "alex.morgan@example.com", "18 Harbour Lane", "Portland", "97201", "US"),
        new("Jordan Blake", "jordan.blake@example.com", "402 Cedar Avenue", "Austin", "78701", "US"),
        new("Priya Raman", "priya.raman@example.com", "7 Kingsway Court", "Manchester", "M1 4BT", "GB"),
        new("Lukas Weber", "lukas.weber@example.com", "Lindenstrasse 22", "Hamburg", "20095", "DE"),
        new("Sofia Marquez", "sofia.marquez@example.com", "Calle Mayor 15", "Valencia", "46001", "ES"),
        new("Noah Fischer", "noah.fischer@example.com", "95 Maple Street", "Toronto", "M5V 2T6", "CA"),
        new("Emma Laurent", "emma.laurent@example.com", "12 Rue des Lilas", "Lyon", "69002", "FR"),
        new("Kenji Sato", "kenji.sato@example.com", "3-8 Sakura Dori", "Osaka", "530-0001", "JP"),
    ];

    /// <summary>Oldest first: the delivered orders are the oldest and the unpaid ones the newest, as in a real shop.</summary>
    private static readonly OrderStatus[] StatusBlocks =
        [OrderStatus.Delivered, OrderStatus.Shipped, OrderStatus.Paid, OrderStatus.Placed];

    public string Module => "ordering";

    public async Task SeedAsync(CancellationToken ct)
    {
        if (await db.Orders.AnyAsync(ct))
        {
            logger.LogInformation("Ordering already has orders; skipping seed");
            return;
        }

        var samples = await catalog.GetSampleAsync(SampleSize, ct);
        if (samples.Count == 0)
        {
            logger.LogWarning("The catalog has no featured active products; no admin demo orders were seeded");
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var total = StatusBlocks.Length * OrdersPerStatus;
        for (var i = 0; i < total; i++)
        {
            orders.Add(await BuildAsync(i, StatusBlocks[i / OrdersPerStatus], samples, now, ct));
        }

        try
        {
            await orders.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Count} admin demo orders", total);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            // Another instance seeded them between our check and our insert; that is the desired end state.
            logger.LogInformation("Admin demo orders were seeded concurrently; skipping seed");
        }
    }

    private async Task<Order> BuildAsync(
        int index, OrderStatus status, IReadOnlyList<VariantSnapshot> samples, DateTime now, CancellationToken ct)
    {
        var customer = Customers[index % Customers.Length];
        var customerId = DeterministicGuid($"seed-customer:{customer.Email}");
        var address = Unwrap(ShippingAddress.Create(
            customer.Name, customer.Line1, null, customer.City, customer.PostalCode, customer.Country));

        // A fewer-than-eight sample is cycled; lines within one order stay distinct.
        var lineCount = Math.Min(1 + index % 3, samples.Count);
        var lines = Enumerable.Range(0, lineCount)
            .Select(k => ToLine(samples[(index + k) % samples.Count], 1 + (index + k) % 2))
            .ToList();

        // Spread across the last 30 days, about 29 hours apart, so the times of day vary too.
        var placedAt = now.AddDays(-30).AddHours(3 + index * 29);
        var number = await orders.NextOrderNumberAsync(ct);
        var order = Unwrap(Order.PlaceHistorical(
            $"{Order.NumberPrefix}{number}", customerId, customer.Email,
            DeterministicGuid($"seed-order:{customer.Email}:{index}"), address, lines, placedAt));

        if (status >= OrderStatus.Paid) Unwrap(order.MarkPaid(placedAt.AddHours(1)));
        if (status >= OrderStatus.Shipped) Unwrap(order.Ship(placedAt.AddDays(1)));
        if (status >= OrderStatus.Delivered) Unwrap(order.Deliver(placedAt.AddDays(3)));
        return order;
    }

    private static NewOrderLine ToLine(VariantSnapshot s, int quantity) =>
        new(s.VariantId, s.ProductName, s.Sku, s.OptionsLabel, s.ThumbnailUrl, s.UnitPrice, quantity);

    /// <summary>SHA-1 of the text, first 16 bytes: the same text always gives the same id.</summary>
    private static Guid DeterministicGuid(string text) => new(SHA1.HashData(Encoding.UTF8.GetBytes(text))[..16]);

    private static T Unwrap<T>(Shop.SharedKernel.Result<T> result) =>
        result.IsSuccess ? result.Value : throw new InvalidOperationException($"Invalid demo order seed: {result.Error.Code}");

    private static void Unwrap(Shop.SharedKernel.Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Invalid demo order seed: {result.Error.Code}");
        }
    }
}
