using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;
using Shop.Api;
using Shop.Api.ErrorHandling;
using Shop.Application.Abstractions;
using Shop.Application.Abstractions.Identity;
using Shop.Infrastructure.Common.Http;
using Shop.Infrastructure.Common.Modules;
using Shop.Infrastructure.Common.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) =>
{
    logger
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
        .Enrich.FromLogContext();

    if (context.HostingEnvironment.IsDevelopment())
    {
        logger.WriteTo.Console();
    }
    else
    {
        logger.WriteTo.Console(new CompactJsonFormatter());
    }
});

// The API is only reachable from Caddy over the private Docker network (no published port in production),
// so trusting every proxy is safe: the only possible sender of X-Forwarded-* is our own reverse proxy.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddProblemDetails();
builder.Services.AddShopRateLimiting();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi(OpenApiConfiguration.Configure);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddDispatcher();
builder.Services.AddEventing(
    typeof(Catalog.Contracts.AssemblyReference).Assembly,
    typeof(Basket.Contracts.AssemblyReference).Assembly,
    typeof(Ordering.Contracts.AssemblyReference).Assembly,
    typeof(Payments.Contracts.AssemblyReference).Assembly,
    typeof(Identity.Contracts.AssemblyReference).Assembly);

foreach (var module in ModuleCatalog.All)
{
    module.Register(builder.Services, builder.Configuration);
}

var connectionString = builder.Configuration.GetConnectionString(ModuleDbContextRegistration.ConnectionStringName)
    ?? throw new InvalidOperationException($"Connection string '{ModuleDbContextRegistration.ConnectionStringName}' is not configured.");
builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

// Before rate limiting, so a forged cross-site request is refused without spending anyone's budget.
app.UseCsrfHeaderCheck();

// After the implicit UseRouting (so endpoint policies are visible) and before the endpoints run.
app.UseRateLimiter();

// Authentication itself (JWT bearer + the Admin policy) is registered by the Identity module.
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference();

app.MapHealthChecks("/health", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

app.MapSystemEndpoints();
foreach (var module in ModuleCatalog.All)
{
    module.MapEndpoints(app);
}

// dotnet-ef builds the host to discover DbContexts; it must not touch the database while doing so.
if (!EF.IsDesignTime && app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await MigrateAsync(app);
}

if (!EF.IsDesignTime && app.Configuration.GetValue("Database:SeedOnStartup", false))
{
    await SeedAsync(app);
}

app.Run();

static async Task MigrateAsync(WebApplication app)
{
    const int maxAttempts = 10;
    var registry = app.Services.GetRequiredService<ModuleDatabaseRegistry>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    foreach (var contextType in registry.ContextTypes)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // One module per DI scope (see ADR-0004): each context gets its own scope.
                using var scope = app.Services.CreateScope();
                var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
                await context.Database.MigrateAsync();
                logger.LogInformation("Migrated {Context}", contextType.Name);
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(ex, "Migrating {Context} failed (attempt {Attempt}/{Max}); retrying in 2s", contextType.Name, attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }
}

static async Task SeedAsync(WebApplication app)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    int count;
    using (var probe = app.Services.CreateScope())
    {
        count = probe.ServiceProvider.GetServices<IModuleSeeder>().Count();
    }

    // Each seeder gets its own scope (one module per DI scope). A failing seeder must stop startup.
    for (var i = 0; i < count; i++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var seeder = scope.ServiceProvider.GetServices<IModuleSeeder>().ElementAt(i);
            await seeder.SeedAsync(app.Lifetime.ApplicationStopping);
            logger.LogInformation("Seeded {Module}", seeder.Module);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Seeding failed (seeder #{Index})", i);
            throw;
        }
    }
}

public partial class Program;
