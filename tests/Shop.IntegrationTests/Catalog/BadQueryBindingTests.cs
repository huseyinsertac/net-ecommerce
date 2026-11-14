using System.Net;
using Microsoft.AspNetCore.Hosting;
using Shouldly;

namespace Shop.IntegrationTests.Catalog;

public sealed class DevelopmentApiFactory : ShopApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Development");
    }
}

/// <summary>Unparseable query values must be a 400 (never a 500) whatever the environment.</summary>
public static class BadQueryBinding
{
    public static TheoryData<string> Urls { get; } = new()
    {
        "/api/catalog/products?page=abc",
        "/api/catalog/products?pageSize=x",
        "/api/catalog/products?minPrice=cheap",
        "/api/catalog/products?maxPrice=1e",
        "/api/catalog/products?inStock=maybe",
        "/api/catalog/products?featured=2",
    };

    public static async Task AssertBadRequest(ShopApiFactory factory, string url)
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}

[Collection(ShopApiCollection.Name)]
public sealed class BadQueryBindingTests(ShopApiFactory factory)
{
    public static TheoryData<string> Urls => BadQueryBinding.Urls;

    [Theory]
    [MemberData(nameof(Urls))]
    public Task NonParseableValue_400_InTestEnvironment(string url) => BadQueryBinding.AssertBadRequest(factory, url);
}

public sealed class BadQueryBindingDevelopmentTests(DevelopmentApiFactory factory) : IClassFixture<DevelopmentApiFactory>
{
    public static TheoryData<string> Urls => BadQueryBinding.Urls;

    [Theory]
    [MemberData(nameof(Urls))]
    public Task NonParseableValue_400_InDevelopment(string url) => BadQueryBinding.AssertBadRequest(factory, url);
}
