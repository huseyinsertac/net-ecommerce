using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Shop.IntegrationTests.Identity;

[Collection(ShopApiCollection.Name)]
public sealed class IdentityPersistenceTests(ShopApiFactory factory)
{
    private static readonly DateTime NowUtc = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Services
    {
        get
        {
            using var _ = factory.CreateClient(); // forces host start (and startup migration)
            return factory.Services;
        }
    }

    private static Email UniqueEmail(string local = "user") =>
        Email.Create($"{local}-{Guid.NewGuid():N}@X.test").Value;

    [Fact]
    public async Task User_RoundTrips_WithRolesAndRefreshTokens()
    {
        var email = UniqueEmail("Admin");
        var user = User.CreateDemoAdmin(email, NowUtc);
        var first = user.IssueRefreshToken("hash-1-" + Guid.NewGuid().ToString("N"), NowUtc, TimeSpan.FromDays(7));
        var rotated = user.Rotate(first.TokenHash, "hash-2-" + Guid.NewGuid().ToString("N"), NowUtc.AddMinutes(1),
            TimeSpan.FromDays(7)).Value;

        await using (var scope = Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            repo.Add(user);
            await repo.SaveChangesAsync(Ct);
        }

        await using (var scope = Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var loaded = await repo.GetByNormalizedEmailAsync(email.Value.ToUpperInvariant(), Ct);

            loaded.ShouldNotBeNull();
            loaded.Id.ShouldBe(user.Id);
            loaded.Email.ShouldBe(email);
            loaded.Email.Value.ShouldBe(email.Value);
            loaded.DisplayName.ShouldBe("Demo Admin");
            loaded.PasswordHash.ShouldBeNull();
            loaded.IsDemo.ShouldBeTrue();
            loaded.CreatedAtUtc.ShouldBe(NowUtc);
            loaded.Roles.ShouldBe([Roles.Admin, Roles.Customer]);

            loaded.RefreshTokens.Count.ShouldBe(2);
            var oldToken = loaded.RefreshTokens.Single(t => t.Id == first.Id);
            oldToken.TokenHash.ShouldBe(first.TokenHash);
            oldToken.UserId.ShouldBe(user.Id);
            oldToken.RevokedAtUtc.ShouldBe(NowUtc.AddMinutes(1));
            oldToken.ReplacedById.ShouldBe(rotated.Id);
            var newToken = loaded.RefreshTokens.Single(t => t.Id == rotated.Id);
            newToken.FamilyId.ShouldBe(first.FamilyId);
            newToken.ExpiresAtUtc.ShouldBe(NowUtc.AddMinutes(1).AddDays(7));
            newToken.RevokedAtUtc.ShouldBeNull();

            (await repo.GetByIdAsync(user.Id, Ct)).ShouldNotBeNull().RefreshTokens.Count.ShouldBe(2);
            (await repo.GetByRefreshTokenHashAsync(rotated.TokenHash, Ct)).ShouldNotBeNull().Id.ShouldBe(user.Id);
            (await repo.GetByRefreshTokenHashAsync("no-such-hash", Ct)).ShouldBeNull();

            // An untouched aggregate must not be reported as modified.
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.ChangeTracker.DetectChanges();
            db.ChangeTracker.HasChanges().ShouldBeFalse();
        }

        (await ScalarAsync<string>("SELECT email_normalized FROM identity.users WHERE id = @id", ("id", user.Id)))
            .ShouldBe(email.Normalized);
        (await ScalarAsync<string>("SELECT email FROM identity.users WHERE id = @id", ("id", user.Id)))
            .ShouldBe(email.Value);
        (await ScalarAsync<string[]>("SELECT roles FROM identity.users WHERE id = @id", ("id", user.Id)))
            .ShouldBe([Roles.Admin, Roles.Customer]);
    }

    [Fact]
    public async Task User_ChangesToRefreshTokens_AreSaved()
    {
        var user = User.CreateDemoCustomer(UniqueEmail(), NowUtc);
        var token = user.IssueRefreshToken("hash-" + Guid.NewGuid().ToString("N"), NowUtc, TimeSpan.FromDays(7));

        await using (var scope = Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            repo.Add(user);
            await repo.SaveChangesAsync(Ct);
        }

        var newHash = "hash-" + Guid.NewGuid().ToString("N");
        await using (var scope = Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var loaded = (await repo.GetByRefreshTokenHashAsync(token.TokenHash, Ct)).ShouldNotBeNull();
            loaded.Rotate(token.TokenHash, newHash, NowUtc.AddHours(1), TimeSpan.FromDays(7)).IsSuccess.ShouldBeTrue();
            await repo.SaveChangesAsync(Ct);
        }

        await using (var scope = Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var loaded = (await repo.GetByIdAsync(user.Id, Ct)).ShouldNotBeNull();
            loaded.RefreshTokens.Count.ShouldBe(2);
            loaded.RefreshTokens.Single(t => t.TokenHash == token.TokenHash).RevokedAtUtc.ShouldBe(NowUtc.AddHours(1));
            loaded.RefreshTokens.Single(t => t.TokenHash == newHash).IsActive(NowUtc.AddHours(2)).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task GetByRefreshTokenHash_LoadsOnlyThePresentedTokensFamily_AndRotationSavesWithinIt()
    {
        var user = User.CreateDemoAdmin(UniqueEmail("shared"), NowUtc);
        var a1 = user.IssueRefreshToken("a1-" + Guid.NewGuid().ToString("N"), NowUtc, TimeSpan.FromDays(7));
        var b1 = user.IssueRefreshToken("b1-" + Guid.NewGuid().ToString("N"), NowUtc, TimeSpan.FromDays(7));
        var c1 = user.IssueRefreshToken("c1-" + Guid.NewGuid().ToString("N"), NowUtc, TimeSpan.FromDays(7));
        var a2 = user.Rotate(a1.TokenHash, "a2-" + Guid.NewGuid().ToString("N"), NowUtc.AddMinutes(1), TimeSpan.FromDays(7)).Value;

        await using (var scope = Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            repo.Add(user);
            await repo.SaveChangesAsync(Ct);
        }

        var a3Hash = "a3-" + Guid.NewGuid().ToString("N");
        await using (var scope = Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var loaded = (await repo.GetByRefreshTokenHashAsync(a1.TokenHash, Ct)).ShouldNotBeNull();

            loaded.RefreshTokens.Select(t => t.Id).ShouldBe([a1.Id, a2.Id], ignoreOrder: true);

            // Replaying the rotated a1 after the grace kills family A only; then rotating a2 fails.
            loaded.Rotate(a1.TokenHash, a3Hash, NowUtc.AddHours(1).AddMinutes(-1), TimeSpan.FromDays(7)).IsFailure.ShouldBeTrue();
            await repo.SaveChangesAsync(Ct);
        }

        await using (var scope = Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var all = (await repo.GetByIdAsync(user.Id, Ct)).ShouldNotBeNull();
            all.RefreshTokens.Count.ShouldBe(4);
            all.RefreshTokens.Where(t => t.FamilyId == a1.FamilyId).ShouldAllBe(t => t.RevokedAtUtc != null);
            all.RefreshTokens.Single(t => t.Id == b1.Id).RevokedAtUtc.ShouldBeNull();
            all.RefreshTokens.Single(t => t.Id == c1.Id).RevokedAtUtc.ShouldBeNull();
        }

        // Two hours on, rotating b1 prunes nothing outside family B (family A's revoked rows were not loaded).
        await using (var scope = Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var loaded = (await repo.GetByRefreshTokenHashAsync(b1.TokenHash, Ct)).ShouldNotBeNull();
            loaded.RefreshTokens.Select(t => t.Id).ShouldBe([b1.Id]);
            loaded.Rotate(b1.TokenHash, "b2-" + Guid.NewGuid().ToString("N"), NowUtc.AddHours(2), TimeSpan.FromDays(7))
                .IsSuccess.ShouldBeTrue();
            await repo.SaveChangesAsync(Ct);
        }

        (await ScalarAsync<long>("SELECT count(*) FROM identity.refresh_tokens WHERE user_id = @id", ("id", user.Id)))
            .ShouldBe(5);

        // A full load (sign-in) prunes family A, revoked more than an hour ago.
        await using (var scope = Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var all = (await repo.GetByIdAsync(user.Id, Ct)).ShouldNotBeNull();
            all.IssueRefreshToken("d1-" + Guid.NewGuid().ToString("N"), NowUtc.AddHours(3), TimeSpan.FromDays(7));
            await repo.SaveChangesAsync(Ct);
        }

        (await ScalarAsync<long>(
                "SELECT count(*) FROM identity.refresh_tokens WHERE user_id = @id AND family_id = @family",
                ("id", user.Id), ("family", a1.FamilyId)))
            .ShouldBe(0);
    }

    [Fact]
    public async Task GetByIdWithoutTokens_LoadsNoRefreshTokens_AndTracksNothing()
    {
        var user = User.CreateDemoCustomer(UniqueEmail(), NowUtc);
        user.IssueRefreshToken("hash-" + Guid.NewGuid().ToString("N"), NowUtc, TimeSpan.FromDays(7));

        await using (var scope = Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            repo.Add(user);
            await repo.SaveChangesAsync(Ct);
        }

        await using (var scope = Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var loaded = (await repo.GetByIdWithoutTokensAsync(user.Id, Ct)).ShouldNotBeNull();

            loaded.Email.ShouldBe(user.Email);
            loaded.Roles.ShouldBe([Roles.Customer]);
            loaded.RefreshTokens.ShouldBeEmpty();
            scope.ServiceProvider.GetRequiredService<IdentityDbContext>().ChangeTracker.Entries().ShouldBeEmpty();
            (await repo.GetByIdWithoutTokensAsync(Guid.NewGuid(), Ct)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task DuplicateNormalizedEmail_ViolatesUniqueIndex_SurfacesAsDuplicateEmailException()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var upper = User.CreateDemoCustomer(Email.Create($"A{suffix}@x.test").Value, NowUtc);
        var lower = User.CreateDemoCustomer(Email.Create($"a{suffix}@x.test").Value, NowUtc);

        await using (var scope = Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            repo.Add(upper);
            await repo.SaveChangesAsync(Ct);
        }

        await using (var scope = Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            repo.Add(lower);

            var ex = await Should.ThrowAsync<DuplicateEmailException>(() => repo.SaveChangesAsync(Ct));
            var pg = ex.InnerException.ShouldBeOfType<DbUpdateException>().InnerException.ShouldBeOfType<PostgresException>();
            pg.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
            pg.ConstraintName.ShouldBe("ix_users_email_normalized");
        }
    }

    [Fact]
    public void PasswordHasher_VerifiesOwnHash_RejectsWrongPassword()
    {
        var hasher = Services.GetRequiredService<IPasswordHasher>();
        var user = User.CreateDemoCustomer(UniqueEmail(), NowUtc);

        var hash = hasher.Hash(user, "correct horse 1");

        hash.ShouldNotBe("correct horse 1");
        hasher.Verify(user, hash, "correct horse 1").ShouldBeTrue();
        hasher.Verify(user, hash, "wrong horse 1").ShouldBeFalse();
        hasher.Hash(user, "correct horse 1").ShouldNotBe(hash); // salted
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }
}
