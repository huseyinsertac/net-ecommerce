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

/// <summary>A tiny global per-IP limit, with its own database. Used by <see cref="GlobalRateLimitTests"/> only.</summary>
public sealed class GlobalLimitApiFactory : ShopApiFactory
{
    public const int ApiPerMinute = 5;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimiting:ApiPerMinute", ApiPerMinute.ToString());
    }
}

/// <summary>The same tiny global limit on a Stripe-configured host, so the webhook route exists.</summary>
public sealed class GlobalLimitStripeApiFactory : Payments.StripeModeApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimiting:ApiPerMinute", GlobalLimitApiFactory.ApiPerMinute.ToString());
    }
}

public sealed class GlobalRateLimitTests(GlobalLimitApiFactory factory, GlobalLimitStripeApiFactory stripeFactory)
    : IClassFixture<GlobalLimitApiFactory>, IClassFixture<GlobalLimitStripeApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Api_requests_over_the_global_limit_get_429()
    {
        using var client = factory.CreateClient();

        for (var i = 0; i < GlobalLimitApiFactory.ApiPerMinute; i++)
        {
            (await client.GetAsync("/api/catalog/categories", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var limited = await client.GetAsync("/api/catalog/categories", Ct);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        JsonDocument.Parse(await limited.Content.ReadAsStringAsync(Ct)).RootElement
            .GetProperty("type").GetString().ShouldBe("rate_limited");
    }

    [Fact]
    public async Task Webhook_and_health_are_not_globally_limited()
    {
        using var client = stripeFactory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            (await client.GetAsync("/health", Ct)).StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);

            using var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/payments/stripe/webhook")
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
            webhook.Headers.Add("Stripe-Signature", "t=1,v1=bad");
            (await client.SendAsync(webhook, Ct)).StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }
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
