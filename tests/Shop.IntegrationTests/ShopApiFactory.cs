using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Shop.IntegrationTests;

/// <summary>Boots the real API in the Production environment against a throwaway Postgres container.</summary>
public class ShopApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Fixed, deliberately NON-SECRET signing key used only by the test host (48 characters).</summary>
    public const string TestSigningKey = "test-only-NOT-A-SECRET-voltlane-jwt-key-48chars!";

    /// <summary>Seeding is off for the shared factory; a dedicated subclass can turn it on.</summary>
    protected virtual bool SeedOnStartup => false;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Shop", _postgres.GetConnectionString());
        builder.UseSetting("Database:SeedOnStartup", SeedOnStartup ? "true" : "false");
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);
        builder.UseSetting("RateLimiting:DemoCustomerPerMinute", "1000");
        builder.UseSetting("RateLimiting:AuthPerMinute", "1000");
        builder.UseSetting("RateLimiting:RefreshPerMinute", "1000");
        builder.UseSetting("RateLimiting:BasketWritesPerMinute", "1000");
        builder.UseSetting("RateLimiting:CheckoutPerMinute", "1000");
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ThrowingEndpointStartupFilter>());
    }

    /// <summary>
    /// Every client from this factory (and from <c>WithWebHostBuilder</c> copies, which reuse this hook) sends the CSRF
    /// header the API requires on non-GET <c>/api/</c> requests, as the SPA does. A test that needs a request without it
    /// removes it from <see cref="HttpClient.DefaultRequestHeaders"/>.
    /// </summary>
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add(CsrfHeaderName, CsrfHeaderValue);
    }

    public const string CsrfHeaderName = "X-Requested-With";
    public const string CsrfHeaderValue = "voltlane";

    /// <summary>
    /// Test-only: appends a middleware AFTER the app's own pipeline, so a request to /test/throw falls through the
    /// endpoints and then throws, travelling back up through the real exception handler.
    /// </summary>
    private sealed class ThrowingEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path == "/test/throw")
                {
                    throw new InvalidOperationException("secret detail");
                }

                await nextMiddleware(context);
            });
        };
    }
}

[CollectionDefinition(Name)]
public sealed class ShopApiCollection : ICollectionFixture<ShopApiFactory>
{
    public const string Name = "ShopApi";
}
