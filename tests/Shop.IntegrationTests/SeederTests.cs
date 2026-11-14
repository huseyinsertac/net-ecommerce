using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Shop.Infrastructure.Common.Modules;
using Shouldly;

namespace Shop.IntegrationTests;

public sealed class SeederTests
{
    [Fact]
    public async Task Startup_RunsRegisteredSeeders_WhenEnabled()
    {
        await using var factory = new SeedingFactory(seedOnStartup: true);
        await factory.InitializeAsync();

        using var _ = factory.CreateClient();

        factory.Seeder.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Startup_DoesNotRunSeeders_WhenDisabled()
    {
        await using var factory = new SeedingFactory(seedOnStartup: false);
        await factory.InitializeAsync();

        using var _ = factory.CreateClient();

        factory.Seeder.Calls.ShouldBe(0);
    }

    private sealed class CountingSeeder : IModuleSeeder
    {
        public int Calls;

        public string Module => "test";

        public Task SeedAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.CompletedTask;
        }
    }

    private sealed class SeedingFactory(bool seedOnStartup) : ShopApiFactory
    {
        public CountingSeeder Seeder { get; } = new();

        protected override bool SeedOnStartup => seedOnStartup;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddSingleton<IModuleSeeder>(Seeder));
        }
    }
}
