using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Infrastructure.Common.Modules;
using Shouldly;

namespace Shop.IntegrationTests.Identity;

public sealed class IdentitySeedTests(DemoSeededApiFactory factory) : IClassFixture<DemoSeededApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Seeder_RunTwice_SingleAdmin()
    {
        using (factory.CreateClient())
        {
            // Forces host start: migrations, then the startup seed (first run).
        }

        // Second and third runs, as a restarted instance would do.
        for (var i = 0; i < 2; i++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var seeder = scope.ServiceProvider.GetServices<IModuleSeeder>().Single(s => s.Module == "identity");
            await seeder.SeedAsync(Ct);
        }

        await using var read = factory.Services.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var admins = await db.Users.Where(u => u.Email.Normalized == "ADMIN@DEMO.VOLTLANE.TEST").ToListAsync(Ct);

        admins.Count.ShouldBe(1);
        admins[0].IsDemo.ShouldBeTrue();
        admins[0].Roles.ShouldBe(["Admin", "Customer"], ignoreOrder: true);
        admins[0].CanLogInWithPassword.ShouldBeFalse();
    }
}
