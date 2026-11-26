using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Shop.IntegrationTests.AuthTestClient;

namespace Shop.IntegrationTests.Identity;

/// <summary>Seeding on (so the demo admin exists) and the limits at their shared test value.</summary>
public sealed class DemoSeededApiFactory : ShopApiFactory
{
    protected override bool SeedOnStartup => true;
}

/// <summary>The real default limit of 5 per minute for demo customers, with its own database.</summary>
public sealed class DemoRateLimitedApiFactory : ShopApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimiting:DemoCustomerPerMinute", "5");
    }
}

public sealed class DemoAccountTests(DemoSeededApiFactory factory) : IClassFixture<DemoSeededApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    [Fact]
    public async Task DemoCustomer_CreatesFreshUserEachTime_IsDemoCustomerRoleOnly()
    {
        using var client = factory.CreateClient();

        var first = await client.PostAsync("/api/identity/demo/customer", null, Ct);
        var second = await client.PostAsync("/api/identity/demo/customer", null, Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        RefreshValue(first).ShouldNotBeNullOrEmpty();
        var a = (await JsonAsync(first)).GetProperty("user");
        var b = (await JsonAsync(second)).GetProperty("user");
        a.GetProperty("id").GetGuid().ShouldNotBe(b.GetProperty("id").GetGuid());
        a.GetProperty("email").GetString()!.ShouldMatch(@"^demo-[0-9a-f]{8}@demo\.voltlane\.test$");
        a.GetProperty("displayName").GetString().ShouldBe("Demo Customer");
        a.GetProperty("isDemo").GetBoolean().ShouldBeTrue();
        a.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldBe(["Customer"]);
    }

    [Fact]
    public async Task DemoCustomer_CannotLogInWithPassword()
    {
        using var client = factory.CreateClient();
        var created = await client.PostAsync("/api/identity/demo/customer", null, Ct);
        var email = (await JsonAsync(created)).GetProperty("user").GetProperty("email").GetString()!;

        var login = await LoginAsync(client, email, "Whatever-123");

        login.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await JsonAsync(login)).GetProperty("type").GetString().ShouldBe("identity.invalid_credentials");
    }

    [Fact]
    public async Task DemoAdmin_ReturnsAdminRole_SameUserEachTime()
    {
        using var client = factory.CreateClient();

        var first = await client.PostAsync("/api/identity/demo/admin", null, Ct);
        var second = await client.PostAsync("/api/identity/demo/admin", null, Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        RefreshValue(first).ShouldNotBeNullOrEmpty();
        var a = (await JsonAsync(first)).GetProperty("user");
        var b = (await JsonAsync(second)).GetProperty("user");
        a.GetProperty("id").GetGuid().ShouldBe(b.GetProperty("id").GetGuid());
        a.GetProperty("email").GetString().ShouldBe("admin@demo.voltlane.test");
        a.GetProperty("isDemo").GetBoolean().ShouldBeTrue();
        a.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldBe(["Admin", "Customer"], ignoreOrder: true);

        // The access token really carries the Admin role.
        Bearer(client, await AccessTokenAsync(first));
        (await client.GetAsync("/api/identity/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DemoAdmin_NotSeeded_503DemoUnavailable()
    {
        // The shared factory runs with seeding off, so there is no admin row.
        await using var unseeded = new ShopApiFactory();
        await unseeded.InitializeAsync();
        using var client = unseeded.CreateClient();

        var response = await client.PostAsync("/api/identity/demo/admin", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await JsonAsync(response)).GetProperty("type").GetString().ShouldBe("identity.demo_unavailable");
    }
}

public sealed class DemoRateLimitTests(DemoRateLimitedApiFactory factory) : IClassFixture<DemoRateLimitedApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<int> UserCountAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.CountAsync(Ct);
    }

    [Fact]
    public async Task DemoCustomer_SixthCallWithinMinute_429_NoUserCreated()
    {
        using var client = factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            (await client.PostAsync("/api/identity/demo/customer", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var before = await UserCountAsync();
        var sixth = await client.PostAsync("/api/identity/demo/customer", null, Ct);

        sixth.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        sixth.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = JsonDocument.Parse(await sixth.Content.ReadAsStringAsync(Ct)).RootElement;
        body.GetProperty("type").GetString().ShouldBe("rate_limited");
        body.GetProperty("title").GetString().ShouldBe("Too many requests. Please wait a minute.");
        RefreshSetCookie(sixth).ShouldBeNull();
        (await UserCountAsync()).ShouldBe(before);
    }
}

public sealed class ReservedDemoDomainTests(DemoSeededApiFactory factory) : IClassFixture<DemoSeededApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("admin@demo.voltlane.test")]
    [InlineData("Someone@DEMO.Voltlane.TEST")]
    public async Task Register_DemoDomain_409EmailTaken_SeededAdminUnaffected(string email)
    {
        using var client = factory.CreateClient();

        var response = await RegisterAsync(client, email);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("type").GetString()
            .ShouldBe("identity.email_taken");
        RefreshSetCookie(response).ShouldBeNull();

        var admin = await client.PostAsync("/api/identity/demo/admin", null, Ct);
        admin.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
