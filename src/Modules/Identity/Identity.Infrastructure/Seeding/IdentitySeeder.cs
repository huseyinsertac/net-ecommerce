using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.Extensions.Logging;
using Shop.Infrastructure.Common.Modules;

namespace Identity.Infrastructure.Seeding;

/// <summary>Creates the shared demo admin when it does not exist yet. Idempotent.</summary>
internal sealed class IdentitySeeder(IUserRepository users, TimeProvider clock, ILogger<IdentitySeeder> logger) : IModuleSeeder
{
    public string Module => "identity";

    public async Task SeedAsync(CancellationToken ct)
    {
        var email = Email.Create(DemoAccounts.AdminEmail).Value;
        if (await users.GetByNormalizedEmailAsync(email.Normalized, ct) is not null)
        {
            logger.LogInformation("Demo admin already exists; skipping seed");
            return;
        }

        users.Add(User.CreateDemoAdmin(email, clock.GetUtcNow().UtcDateTime));
        try
        {
            await users.SaveChangesAsync(ct);
            logger.LogInformation("Seeded demo admin");
        }
        catch (DuplicateEmailException)
        {
            // Another instance seeded it between our check and our insert; that is the desired end state.
            logger.LogInformation("Demo admin was created concurrently; skipping seed");
        }
    }
}
