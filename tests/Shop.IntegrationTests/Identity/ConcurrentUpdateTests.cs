using System.Net;
using System.Text.Json;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Shop.IntegrationTests.AuthTestClient;

namespace Shop.IntegrationTests.Identity;

/// <summary>
/// When every attempt loses the optimistic-concurrency race, login, refresh and logout answer 409
/// <c>identity.concurrent_update</c> ("Please try again.") instead of a 500.
/// </summary>
[Collection(ShopApiCollection.Name)]
public sealed class ConcurrentUpdateTests(ShopApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Reads pass through; every save reports a lost race.</summary>
    private sealed class AlwaysConflictingRepository(IUserRepository inner) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) => inner.GetByIdAsync(id, ct);
        public Task<User?> GetByIdWithoutTokensAsync(Guid id, CancellationToken ct) => inner.GetByIdWithoutTokensAsync(id, ct);
        public Task<User?> GetByNormalizedEmailAsync(string normalized, CancellationToken ct) => inner.GetByNormalizedEmailAsync(normalized, ct);
        public Task<User?> GetByRefreshTokenHashAsync(string tokenHash, CancellationToken ct) => inner.GetByRefreshTokenHashAsync(tokenHash, ct);
        public void Add(User user) => inner.Add(user);
        public Task SaveChangesAsync(CancellationToken ct) => throw new ConcurrencyConflictException(new InvalidOperationException("forced"));
    }

    private HttpClient ConflictingClient() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            // Keep the real (internal) repository as a concrete registration and put the conflicting decorator in front.
            var real = services.Single(d => d.ServiceType == typeof(IUserRepository)).ImplementationType!;
            services.AddScoped(real);
            services.AddScoped<IUserRepository>(sp => new AlwaysConflictingRepository((IUserRepository)sp.GetRequiredService(real)));
        })).CreateClient();

    private static async Task AssertConcurrentAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        body.GetProperty("type").GetString().ShouldBe("identity.concurrent_update");
        body.GetProperty("title").GetString().ShouldBe("Please try again.");
    }

    [Fact]
    public async Task Login_ConflictsOnEverySave_409ConcurrentUpdate()
    {
        using var normal = factory.CreateClient();
        var email = NewEmail();
        (await RegisterAsync(normal, email)).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var client = ConflictingClient();
        await AssertConcurrentAsync(await LoginAsync(client, email));
    }

    [Fact]
    public async Task Refresh_ConflictsOnEverySave_409_CookieKept()
    {
        using var normal = factory.CreateClient();
        var refresh = RefreshValue(await RegisterAsync(normal, NewEmail()))!;

        using var client = ConflictingClient();
        var response = await PostWithRefreshAsync(client, "/api/identity/refresh", refresh);

        await AssertConcurrentAsync(response);
        RefreshSetCookie(response).ShouldBeNull(); // the still-valid cookie is not cleared
    }

    [Fact]
    public async Task Logout_ConflictsOnEverySave_409_ButCookieCleared()
    {
        using var normal = factory.CreateClient();
        var refresh = RefreshValue(await RegisterAsync(normal, NewEmail()))!;

        using var client = ConflictingClient();
        var response = await PostWithRefreshAsync(client, "/api/identity/logout", refresh);

        await AssertConcurrentAsync(response);
        RefreshValue(response).ShouldBe(string.Empty); // cookie cleared even though the server-side revoke did not commit
    }
}
