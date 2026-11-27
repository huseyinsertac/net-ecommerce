using Shop.Infrastructure.Common.Modules;

namespace Shop.Api;

/// <summary>Runs every registered <see cref="IModuleSeeder"/>, in registration (module) order, at startup and after a demo reset.</summary>
public static class ModuleSeeding
{
    public static async Task RunAllAsync(IServiceProvider services, ILogger logger, CancellationToken ct)
    {
        int count;
        using (var probe = services.CreateScope())
        {
            count = probe.ServiceProvider.GetServices<IModuleSeeder>().Count();
        }

        // Each seeder gets its own scope (one module per DI scope). A failing seeder stops the run.
        for (var i = 0; i < count; i++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var seeder = scope.ServiceProvider.GetServices<IModuleSeeder>().ElementAt(i);
                await seeder.SeedAsync(ct);
                logger.LogInformation("Seeded {Module}", seeder.Module);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Seeding failed (seeder #{Index})", i);
                throw;
            }
        }
    }
}
