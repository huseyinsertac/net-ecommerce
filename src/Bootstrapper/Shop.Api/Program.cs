using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;
using Shop.Api;
using Shop.Api.DemoReset;
using Shop.Api.ErrorHandling;
using Shop.Application.Abstractions;
using Shop.Application.Abstractions.Identity;
using Shop.Infrastructure.Common.Http;
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

// After the modules: the reset needs their DbContexts and seeders. The nightly timer is added only when enabled.
builder.Services.AddDemoReset(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString(ModuleDbContextRegistration.ConnectionStringName)
    ?? throw new InvalidOperationException($"Connection string '{ModuleDbContextRegistration.ConnectionStringName}' is not configured.");
builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);

var app = builder.Build();

// `dotnet Shop.Api.dll reset-demo`: migrate, wipe, re-seed and exit. The host is never started, so neither the web
// server nor any hosted service (outbox processors, expiry job, nightly timer) runs in this process.
if (!EF.IsDesignTime && args.Contains(DemoResetCommand.Name, StringComparer.Ordinal))
{
    var exitCode = await DemoResetCommand.RunAsync(app.Services, CancellationToken.None);
    await app.DisposeAsync(); // flushes the logs
    return exitCode;
}

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
    await ModuleMigrations.RunAllAsync(app.Services, app.Services.GetRequiredService<ILogger<Program>>(), CancellationToken.None);
}

if (!EF.IsDesignTime && app.Configuration.GetValue("Database:SeedOnStartup", false))
{
    // A failing seeder stops startup.
    await ModuleSeeding.RunAllAsync(app.Services, app.Services.GetRequiredService<ILogger<Program>>(), app.Lifetime.ApplicationStopping);
}

app.Run();
return 0;

public partial class Program;
