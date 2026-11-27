using System.Net;
using Catalog.Contracts.Variants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Persistence;
using Shop.Api.DemoReset;
using Shop.Infrastructure.Common.Modules;
using Shouldly;
using static Shop.IntegrationTests.Ordering.OrderingTestClient;

namespace Shop.IntegrationTests.DemoReset;

/// <summary>
/// Seeding on, with its own Postgres container: these tests truncate every module table, so they must never share a
/// database with other tests.
/// </summary>
public sealed class DemoResetApiFactory : ShopApiFactory
{
    protected override bool SeedOnStartup => true;
}

public sealed class DemoResetTests(DemoResetApiFactory factory) : IClassFixture<DemoResetApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Services
    {
        get
        {
            using var _ = factory.CreateClient(); // forces host start (migrations, then the startup seed)
            return factory.Services;
        }
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task ExecuteAsync(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task<List<Order>> OrdersAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Orders
            .AsNoTracking().Include(o => o.Lines).ToListAsync(Ct);
    }

    private sealed record Counts(long Users, long Products, long Variants, long Categories, long Orders, long Baskets);

    private async Task<Counts> CountsAsync() => new(
        await ScalarAsync<long>("SELECT count(*) FROM identity.users"),
        await ScalarAsync<long>("SELECT count(*) FROM catalog.products"),
        await ScalarAsync<long>("SELECT count(*) FROM catalog.product_variants"),
        await ScalarAsync<long>("SELECT count(*) FROM catalog.categories"),
        await ScalarAsync<long>("SELECT count(*) FROM ordering.orders"),
        await ScalarAsync<long>("SELECT count(*) FROM basket.baskets"));

    private async Task<(string Slug, string Name)> FirstProductAsync()
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT slug, name FROM catalog.products ORDER BY slug LIMIT 1", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).ShouldBeTrue();
        return (reader.GetString(0), reader.GetString(1));
    }

    private Task<string> ProductNameAsync(string slug) =>
        ScalarAsync<string>($"SELECT name FROM catalog.products WHERE slug = '{slug.Replace("'", "''", StringComparison.Ordinal)}'");

    private Task RenameProductAsync(string slug, string name) =>
        ExecuteAsync("UPDATE catalog.products SET name = @name WHERE slug = @slug",
            new NpgsqlParameter("name", name), new NpgsqlParameter("slug", slug));

    /// <summary>Polls until no module outbox holds an unprocessed row, so no event from a live flow lands after a reset.</summary>
    private async Task WaitForOutboxesDrainedAsync()
    {
        const string pending = """
            SELECT (SELECT count(*) FROM catalog.outbox_messages WHERE processed_on_utc IS NULL)
                 + (SELECT count(*) FROM basket.outbox_messages WHERE processed_on_utc IS NULL)
                 + (SELECT count(*) FROM ordering.outbox_messages WHERE processed_on_utc IS NULL)
                 + (SELECT count(*) FROM payments.outbox_messages WHERE processed_on_utc IS NULL)
                 + (SELECT count(*) FROM identity.outbox_messages WHERE processed_on_utc IS NULL)
            """;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (await ScalarAsync<long>(pending) > 0)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "outboxes did not drain");
            await Task.Delay(100, Ct);
        }
    }

    private async Task PlaceRealOrderAsync()
    {
        using var shopper = await SignInAsync(factory, Ct);
        VariantSnapshot sample;
        await using (var scope = Services.CreateAsyncScope())
        {
            sample = (await scope.ServiceProvider.GetRequiredService<ICatalogVariantQuery>().GetSampleAsync(1, Ct)).Single();
        }

        await AddToBasketAsync(shopper.Client, sample.VariantId, 1, Ct);
        await PlaceAsync(shopper.Client, sample.UnitPrice, Ct);
    }

    private DemoDataReset Reset => Services.GetRequiredService<DemoDataReset>();

    [Fact]
    public async Task Reset_wipes_user_data_and_reseeds()
    {
        using var client = factory.CreateClient();
        var email = AuthTestClient.NewEmail();
        (await AuthTestClient.RegisterAsync(client, email)).EnsureSuccessStatusCode();
        await PlaceRealOrderAsync();
        var (slug, seededName) = await FirstProductAsync();
        await RenameProductAsync(slug, "Renamed by a visitor");
        await WaitForOutboxesDrainedAsync();

        await Reset.ResetAsync(Ct);

        (await AuthTestClient.LoginAsync(client, email)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ProductNameAsync(slug)).ShouldBe(seededName);
        (await client.PostAsync("/api/identity/demo/admin", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var orders = await OrdersAsync();
        orders.Count.ShouldBe(24);
        orders.OrderBy(o => o.PlacedAtUtc).First().OrderNumber.ShouldBe("VL-100001");
        orders.Select(o => o.OrderNumber).Order(StringComparer.Ordinal)
            .ShouldBe(Enumerable.Range(100001, 24).Select(n => $"VL-{n}"));
        (await ScalarAsync<long>("SELECT count(*) FROM basket.baskets")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM identity.refresh_tokens")).ShouldBe(1); // the admin sign-in above
    }

    [Fact]
    public async Task Reset_is_repeatable()
    {
        await Reset.ResetAsync(Ct);
        var first = await CountsAsync();
        var firstNumbers = (await OrdersAsync()).Select(o => o.OrderNumber).Order(StringComparer.Ordinal).ToList();

        await Reset.ResetAsync(Ct);
        var second = await CountsAsync();
        var secondNumbers = (await OrdersAsync()).Select(o => o.OrderNumber).Order(StringComparer.Ordinal).ToList();

        second.ShouldBe(first);
        secondNumbers.ShouldBe(firstNumbers);
        first.Users.ShouldBe(1); // the demo admin only
        first.Orders.ShouldBe(24);
        first.Products.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Reset_rolls_back_when_a_truncate_fails()
    {
        using var client = factory.CreateClient();
        var email = AuthTestClient.NewEmail();
        (await AuthTestClient.RegisterAsync(client, email)).EnsureSuccessStatusCode();
        var (slug, _) = await FirstProductAsync();
        await RenameProductAsync(slug, "Kept after a failed reset");
        var before = await CountsAsync();
        var reset = ActivatorUtilities.CreateInstance<DemoDataReset>(
            Services, Options.Create(new DemoResetOptions { StatementTimeout = TimeSpan.FromSeconds(1) }));

        await using (var locker = new NpgsqlConnection(factory.ConnectionString))
        {
            await locker.OpenAsync(Ct);
            await using var hold = await locker.BeginTransactionAsync(Ct);
            await using (var lockCommand = new NpgsqlCommand("LOCK TABLE ordering.orders IN ACCESS EXCLUSIVE MODE", locker, hold))
            {
                await lockCommand.ExecuteNonQueryAsync(Ct);
            }

            var failure = await Should.ThrowAsync<PostgresException>(() => reset.ResetAsync(Ct));
            failure.SqlState.ShouldBe(PostgresErrorCodes.QueryCanceled);
            await hold.RollbackAsync(Ct);
        }

        (await CountsAsync()).ShouldBe(before);
        (await ProductNameAsync(slug)).ShouldBe("Kept after a failed reset");
        (await AuthTestClient.LoginAsync(client, email)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_seed_has_six_of_each_status_and_touches_no_stock()
    {
        await Reset.ResetAsync(Ct);
        var now = DateTime.UtcNow;

        var orders = await OrdersAsync();

        orders.Count.ShouldBe(24);
        orders.ShouldAllBe(o => o.IsHistorical);
        orders.GroupBy(o => o.Status).ToDictionary(g => g.Key, g => g.Count()).ShouldBe(new Dictionary<OrderStatus, int>
        {
            [OrderStatus.Placed] = 6,
            [OrderStatus.Paid] = 6,
            [OrderStatus.Shipped] = 6,
            [OrderStatus.Delivered] = 6,
        }, ignoreOrder: true);
        var emails = orders.Select(o => o.CustomerEmail).Distinct().ToList();
        emails.Count.ShouldBe(8);
        emails.ShouldContain("alex.morgan@example.com");
        emails.ShouldAllBe(e => e.EndsWith("@example.com"));
        orders.Select(o => o.CustomerId).Distinct().Count().ShouldBe(8);
        orders.ShouldAllBe(o => o.PlacedAtUtc > now.AddDays(-30) && o.PlacedAtUtc < now);
        orders.ShouldAllBe(o => o.History.All(h => h.AtUtc <= now));
        orders.ShouldAllBe(o => o.Lines.Count > 0);

        (await ScalarAsync<long>("SELECT count(*) FROM catalog.stock_reservations")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT coalesce(sum(stock_reserved), 0) FROM catalog.product_variants")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM ordering.outbox_messages")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM payments.payments")).ShouldBe(0);
    }

    [Fact]
    public async Task Ordering_seed_is_idempotent()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var seeder = scope.ServiceProvider.GetServices<IModuleSeeder>().Single(s => s.Module == "ordering");
            await seeder.SeedAsync(Ct);
        }

        (await OrdersAsync()).Count.ShouldBe(24);
    }

    [Fact]
    public void Seeders_run_catalog_before_ordering()
    {
        using var scope = Services.CreateScope();
        var modules = scope.ServiceProvider.GetServices<IModuleSeeder>().Select(s => s.Module).ToList();

        modules.ShouldContain("catalog");
        modules.ShouldContain("ordering");
        modules.IndexOf("catalog").ShouldBeLessThan(modules.IndexOf("ordering"));
    }

    [Fact]
    public async Task Cli_command_migrates_resets_seeds_and_returns_zero()
    {
        using var client = factory.CreateClient();
        var email = AuthTestClient.NewEmail();
        (await AuthTestClient.RegisterAsync(client, email)).EnsureSuccessStatusCode();

        var exitCode = await DemoResetCommand.RunAsync(Services, Ct);

        exitCode.ShouldBe(0);
        (await AuthTestClient.LoginAsync(client, email)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await OrdersAsync()).Count.ShouldBe(24);
    }

    [Fact]
    public void Nightly_service_is_not_registered_by_default()
    {
        Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<NightlyResetService>().ShouldBeEmpty();
    }
}

public sealed class NightlyResetScheduleTests
{
    private static readonly TimeSpan TwoAm = TimeSpan.FromHours(2);

    [Fact]
    public void Next_run_is_the_next_0200_utc()
    {
        NightlyResetService.NextRunUtc(new DateTimeOffset(2026, 11, 27, 1, 59, 0, TimeSpan.Zero), TwoAm)
            .ShouldBe(new DateTimeOffset(2026, 11, 27, 2, 0, 0, TimeSpan.Zero));
        NightlyResetService.NextRunUtc(new DateTimeOffset(2026, 11, 27, 2, 0, 1, TimeSpan.Zero), TwoAm)
            .ShouldBe(new DateTimeOffset(2026, 11, 28, 2, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Exactly_at_the_time_waits_for_the_next_day()
    {
        NightlyResetService.NextRunUtc(new DateTimeOffset(2026, 11, 27, 2, 0, 0, TimeSpan.Zero), TwoAm)
            .ShouldBe(new DateTimeOffset(2026, 11, 28, 2, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Non_utc_offsets_are_converted_first()
    {
        // 03:30 at +02:00 is 01:30 UTC, so the same UTC day's 02:00.
        NightlyResetService.NextRunUtc(new DateTimeOffset(2026, 11, 27, 3, 30, 0, TimeSpan.FromHours(2)), TwoAm)
            .ShouldBe(new DateTimeOffset(2026, 11, 27, 2, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Year_end_rolls_over()
    {
        NightlyResetService.NextRunUtc(new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero), TwoAm)
            .ShouldBe(new DateTimeOffset(2027, 1, 1, 2, 0, 0, TimeSpan.Zero));
    }
}
