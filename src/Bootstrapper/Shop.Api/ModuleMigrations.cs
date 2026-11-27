using Microsoft.EntityFrameworkCore;
using Shop.Infrastructure.Common.Persistence;

namespace Shop.Api;

/// <summary>Applies every module's EF migrations, retrying while the database is still starting.</summary>
public static class ModuleMigrations
{
    private const int MaxAttempts = 10;

    public static async Task RunAllAsync(IServiceProvider services, ILogger logger, CancellationToken ct)
    {
        var registry = services.GetRequiredService<ModuleDatabaseRegistry>();

        foreach (var contextType in registry.ContextTypes)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    // One module per DI scope (see ADR-0004): each context gets its own scope.
                    await using var scope = services.CreateAsyncScope();
                    var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
                    await context.Database.MigrateAsync(ct);
                    logger.LogInformation("Migrated {Context}", contextType.Name);
                    break;
                }
                catch (Exception ex) when (attempt < MaxAttempts && !ct.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Migrating {Context} failed (attempt {Attempt}/{Max}); retrying in 2s", contextType.Name, attempt, MaxAttempts);
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
            }
        }
    }
}
