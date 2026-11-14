namespace Shop.Infrastructure.Common.Modules;

/// <summary>
/// Seeds a module's data. The host runs every registered seeder after all migrations have completed (when
/// <c>Database:SeedOnStartup</c> is true), each in its own DI scope. Implementations must be idempotent.
/// </summary>
public interface IModuleSeeder
{
    string Module { get; }

    Task SeedAsync(CancellationToken ct);
}
