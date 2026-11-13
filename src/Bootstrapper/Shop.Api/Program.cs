using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;
using Shop.Api;
using Shop.Api.ErrorHandling;
using Shop.Application.Abstractions;
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

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();

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

app.UseSerilogRequestLogging();
app.UseExceptionHandler();

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
var isEfTooling = string.Equals(System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name, "ef", StringComparison.Ordinal);
if (!isEfTooling && app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await MigrateAsync(app);
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

public partial class Program;
