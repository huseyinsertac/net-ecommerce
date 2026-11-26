using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Shouldly;
using static Shop.IntegrationTests.AuthTestClient;

namespace Shop.IntegrationTests;

/// <summary>Tight refresh and basket-write limits, with its own database. Used by <see cref="RateLimitTests"/> only.</summary>
public sealed class TightLimitsApiFactory : ShopApiFactory
{
    public const int RefreshPerMinute = 2;
    public const int BasketWritesPerMinute = 3;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimiting:RefreshPerMinute", RefreshPerMinute.ToString());
        builder.UseSetting("RateLimiting:BasketWritesPerMinute", BasketWritesPerMinute.ToString());
    }
}

public sealed class RateLimitTests(TightLimitsApiFactory factory) : IClassFixture<TightLimitsApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BasketWrites_OverThePerIpLimit_429RateLimited_ReadsUnaffected()
    {
        using var client = factory.CreateClient();
        var variant = Guid.NewGuid(); // unknown: each write is a cheap 200/404, which still counts

        for (var i = 0; i < TightLimitsApiFactory.BasketWritesPerMinute; i++)
        {
            (await client.DeleteAsync($"/api/basket/items/{variant}", Ct)).StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }

        var put = await client.PutAsJsonAsync($"/api/basket/items/{variant}", new { quantity = 1 }, Ct);
        var merge = await client.PostAsync("/api/basket/merge", null, Ct);
        var clear = await client.DeleteAsync("/api/basket", Ct);

        foreach (var response in new[] { put, merge, clear })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
            JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement
                .GetProperty("type").GetString().ShouldBe("rate_limited");
        }

        (await client.GetAsync("/api/basket", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RefreshWithoutCookie_DoesNotSpendTheRefreshBudget()
    {
        using var client = factory.CreateClient();
        var refresh = RefreshValue(await RegisterAsync(client, NewEmail()))!;

        for (var i = 0; i < TightLimitsApiFactory.RefreshPerMinute + 3; i++)
        {
            (await PostWithRefreshAsync(client, "/api/identity/refresh", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var withCookie = await PostWithRefreshAsync(client, "/api/identity/refresh", refresh);
        withCookie.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Calls that do carry a cookie are still limited.
        var next = RefreshValue(withCookie)!;
        (await PostWithRefreshAsync(client, "/api/identity/refresh", next)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostWithRefreshAsync(client, "/api/identity/refresh", "garbage")).StatusCode
            .ShouldBe(HttpStatusCode.TooManyRequests);
    }
}
