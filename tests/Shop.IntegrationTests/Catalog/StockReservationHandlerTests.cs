using System.Text.Json;
using Catalog.Contracts.Events;
using Catalog.Domain.Products;
using Catalog.Domain.Reservations;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Contracts.Events;
using Payments.Contracts.Events;
using Shop.Infrastructure.Common.Events;
using Shop.SharedKernel;
using Shouldly;

namespace Shop.IntegrationTests.Catalog;

[Collection(ShopApiCollection.Name)]
public sealed class StockReservationHandlerTests(ShopApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IServiceProvider Services
    {
        get
        {
            using var _ = factory.CreateClient(); // forces host start (and startup migration)
            return factory.Services;
        }
    }

    private async Task<Product> SeedLaptop(int stock)
    {
        var suffix = CatalogTestData.NewSuffix();
        var category = CatalogTestData.NewCategory($"laptops-{suffix}", "Laptops", CatalogTestData.LaptopSchema);
        var product = CatalogTestData.Laptop(category, suffix, stockOnHand: stock);
        product.Publish().IsSuccess.ShouldBeTrue();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Categories.Add(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(Ct);
        return product;
    }

    private async Task<Product> SeedHeadphones(int stock)
    {
        var suffix = CatalogTestData.NewSuffix();
        var category = CatalogTestData.NewCategory($"headphones-{suffix}", "Headphones", CatalogTestData.HeadphoneSchema);
        var product = CatalogTestData.Headphones(category, suffix, stock);
        product.Publish().IsSuccess.ShouldBeTrue();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Categories.Add(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(Ct);
        return product;
    }

    private Task Deliver(IIntegrationEvent e) => Services.GetRequiredService<IEventBus>().PublishAsync(e, Ct);

    private static OrderPlaced Order(Guid orderId, params (Guid VariantId, int Quantity)[] lines) =>
        new(orderId, Guid.NewGuid(), [.. lines.Select(l => new OrderPlacedLine(l.VariantId, l.Quantity))], 100m, "USD");

    private async Task<ProductVariant[]> Variants(Guid productId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var product = await db.Products.AsNoTracking().Include(p => p.Variants).SingleAsync(p => p.Id == productId, Ct);
        return [.. product.Variants];
    }

    private async Task<StockReservation?> Reservation(Guid orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        return await db.Set<StockReservation>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == orderId, Ct);
    }

    private async Task<List<T>> Outbox<T>(Guid orderId) where T : IntegrationEvent
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var type = typeof(T).FullName;
        var rows = await db.OutboxMessages.AsNoTracking().Where(m => m.Type == type).ToListAsync(Ct);
        return [.. rows
            .Select(r => JsonSerializer.Deserialize<T>(r.Content, Json)!)
            .Where(e => e switch
            {
                StockReserved s => s.OrderId == orderId,
                StockReservationFailed f => f.OrderId == orderId,
                _ => false,
            })];
    }

    [Fact]
    public async Task Reserves_all_lines_and_publishes_StockReserved()
    {
        var product = await SeedLaptop(stock: 5);
        var (a, b) = (product.Variants[0].Id, product.Variants[1].Id);
        var orderId = Guid.NewGuid();

        await Deliver(Order(orderId, (a, 2), (b, 3)));

        var variants = await Variants(product.Id);
        variants.Single(v => v.Id == a).StockReserved.ShouldBe(2);
        variants.Single(v => v.Id == b).StockReserved.ShouldBe(3);
        var reservation = await Reservation(orderId);
        reservation.ShouldNotBeNull();
        reservation.Status.ShouldBe(ReservationStatus.Reserved);
        reservation.Lines.Count.ShouldBe(2);
        (await Outbox<StockReserved>(orderId)).Count.ShouldBe(1);
        (await Outbox<StockReservationFailed>(orderId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Insufficient_stock_on_one_line_reserves_nothing_and_publishes_failure_with_its_sku()
    {
        var product = await SeedLaptop(stock: 5);
        var (a, b) = (product.Variants[0], product.Variants[1]);
        var orderId = Guid.NewGuid();

        await Deliver(Order(orderId, (a.Id, 2), (b.Id, 6)));

        (await Variants(product.Id)).ShouldAllBe(v => v.StockReserved == 0);
        (await Reservation(orderId)).ShouldBeNull();
        var failure = (await Outbox<StockReservationFailed>(orderId)).ShouldHaveSingleItem();
        failure.Skus.ShouldBe([b.Sku]);
        (await Outbox<StockReserved>(orderId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_variant_fails_with_its_id_as_sku()
    {
        var unknown = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await Deliver(Order(orderId, (unknown, 1)));

        (await Outbox<StockReservationFailed>(orderId)).ShouldHaveSingleItem().Skus.ShouldBe([unknown.ToString()]);
    }

    [Fact]
    public async Task Archived_product_line_fails_reservation()
    {
        var product = await SeedLaptop(stock: 5);
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var tracked = await db.Products.Include(p => p.Variants).SingleAsync(p => p.Id == product.Id, Ct);
            tracked.Archive().IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(Ct);
        }

        var orderId = Guid.NewGuid();
        await Deliver(Order(orderId, (product.Variants[0].Id, 1)));

        (await Variants(product.Id)).ShouldAllBe(v => v.StockReserved == 0);
        (await Outbox<StockReservationFailed>(orderId)).ShouldHaveSingleItem().Skus.ShouldBe([product.Variants[0].Sku]);
    }

    [Fact]
    public async Task Duplicate_OrderPlaced_reserves_once()
    {
        var product = await SeedLaptop(stock: 5);
        var variant = product.Variants[0].Id;
        var orderId = Guid.NewGuid();
        var first = Order(orderId, (variant, 2));

        await Deliver(first);
        await Deliver(first); // same event id: the inbox skips it
        await Deliver(Order(orderId, (variant, 2))); // new event id, same order: the existing reservation wins

        (await Variants(product.Id)).Single(v => v.Id == variant).StockReserved.ShouldBe(2);
        (await Outbox<StockReserved>(orderId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Commit_moves_reserved_out_of_on_hand()
    {
        var product = await SeedLaptop(stock: 5);
        var variant = product.Variants[0].Id;
        var orderId = Guid.NewGuid();
        await Deliver(Order(orderId, (variant, 2)));

        await Deliver(new PaymentSucceeded(orderId, Guid.NewGuid()));

        var v = (await Variants(product.Id)).Single(x => x.Id == variant);
        (v.StockOnHand, v.StockReserved).ShouldBe((3, 0));
        (await Reservation(orderId))!.Status.ShouldBe(ReservationStatus.Committed);

        // A late failure for an already committed order changes nothing.
        await Deliver(new PaymentFailed(orderId, Guid.NewGuid(), "late"));
        var after = (await Variants(product.Id)).Single(x => x.Id == variant);
        (after.StockOnHand, after.StockReserved).ShouldBe((3, 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Release_returns_reserved_to_available(bool expired)
    {
        var product = await SeedLaptop(stock: 5);
        var variant = product.Variants[0].Id;
        var orderId = Guid.NewGuid();
        await Deliver(Order(orderId, (variant, 2)));

        await Deliver(expired
            ? new PaymentExpired(orderId, Guid.NewGuid())
            : new PaymentFailed(orderId, Guid.NewGuid(), "declined"));

        var v = (await Variants(product.Id)).Single(x => x.Id == variant);
        (v.StockOnHand, v.StockReserved, v.Available).ShouldBe((5, 0, 5));
        (await Reservation(orderId))!.Status.ShouldBe(ReservationStatus.Released);
    }

    [Fact]
    public async Task Payment_events_without_a_reservation_do_nothing()
    {
        var orderId = Guid.NewGuid();
        await Deliver(new PaymentSucceeded(orderId, Guid.NewGuid()));
        await Deliver(new PaymentFailed(orderId, Guid.NewGuid(), "x"));
        await Deliver(new PaymentExpired(orderId, Guid.NewGuid()));
        (await Reservation(orderId)).ShouldBeNull();
    }

    [Fact]
    public async Task Concurrent_orders_for_last_unit_reserve_exactly_one()
    {
        var product = await SeedHeadphones(stock: 1);
        var variant = product.Variants.Single().Id;
        var (o1, o2) = (Guid.NewGuid(), Guid.NewGuid());

        // A concurrency conflict propagates (the outbox would retry it), so retry a failed delivery until it completes.
        async Task DeliverWithRetry(OrderPlaced e)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await Deliver(e);
                    return;
                }
                catch (Exception) when (attempt < 10)
                {
                    await Task.Delay(50, Ct);
                }
            }
        }

        await Task.WhenAll(DeliverWithRetry(Order(o1, (variant, 1))), DeliverWithRetry(Order(o2, (variant, 1))));

        var reserved = (await Outbox<StockReserved>(o1)).Count + (await Outbox<StockReserved>(o2)).Count;
        var failed = (await Outbox<StockReservationFailed>(o1)).Count + (await Outbox<StockReservationFailed>(o2)).Count;
        reserved.ShouldBe(1);
        failed.ShouldBe(1);
        (await Variants(product.Id)).Single().StockReserved.ShouldBe(1);
    }
}
