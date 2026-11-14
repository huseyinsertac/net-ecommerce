using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace Shop.IntegrationTests.Catalog;

/// <summary>A fresh API + Postgres container seeded with the search dataset, so exact results can be asserted.</summary>
public sealed class CatalogEndpointFixture : IAsyncLifetime
{
    public ShopApiFactory Factory { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await Factory.InitializeAsync();
        using (Factory.CreateClient())
        {
            // Forces host start (and the startup migration).
        }

        await CatalogTestData.SeedSearchDatasetAsync(Factory.Services, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await Factory.DisposeAsync();
}

public sealed class CatalogEndpointTests(CatalogEndpointFixture fixture) : IClassFixture<CatalogEndpointFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client() => fixture.Factory.CreateClient();

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    [Fact]
    public async Task GetCategories_200_Tree()
    {
        var response = await Client().GetAsync("/api/catalog/categories", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roots = await Json(response);
        var electronics = roots.EnumerateArray().Single(c => c.GetProperty("slug").GetString() == "electronics");
        electronics.GetProperty("productCount").GetInt32().ShouldBe(7);
        electronics.GetProperty("children").EnumerateArray().Select(c => c.GetProperty("slug").GetString())
            .ShouldBe(["laptops", "tablets"]);
    }

    [Fact]
    public async Task GetProducts_QueryStringBinding_AllFilters()
    {
        var url = "/api/catalog/products?category=laptops&brand=Arcwing&brand=Nimbus&minPrice=500&maxPrice=2000"
            + "&featured=true&spec=ram_gb:16..64&spec=os:" + Uri.EscapeDataString("Windows 11,Linux")
            + "&spec=touchscreen:true&sort=priceAsc&page=1&pageSize=10";

        var response = await Client().GetAsync(url, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await Json(response);
        page.GetProperty("totalCount").GetInt32().ShouldBe(2);
        page.GetProperty("pageSize").GetInt32().ShouldBe(10);
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slug").GetString())
            .ShouldBe(["nimbus-book-15", "arcwing-14"]);
    }

    [Fact]
    public async Task GetProducts_InStockTrue_ExcludesFullyReservedProduct()
    {
        var page = await Json(await Client().GetAsync("/api/catalog/products?category=laptops&inStock=true&featured=true", Ct));

        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slug").GetString()).ShouldBe(["arcwing-14"]);
    }

    [Fact]
    public async Task GetProducts_Defaults_AreFirstPageOf24()
    {
        var page = await Json(await Client().GetAsync("/api/catalog/products", Ct));

        page.GetProperty("page").GetInt32().ShouldBe(1);
        page.GetProperty("pageSize").GetInt32().ShouldBe(24);
        page.GetProperty("totalCount").GetInt32().ShouldBe(10);
    }

    [Theory]
    [InlineData("spec=ram_gb")]
    [InlineData("spec=:16")]
    [InlineData("spec=ram_gb:..")]
    [InlineData("spec=ram_gb:a..b")]
    public async Task GetProducts_MalformedSpec_400_WithSpecError(string spec)
    {
        var response = await Client().GetAsync($"/api/catalog/products?category=laptops&{spec}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await Json(response);
        problem.GetProperty("type").GetString().ShouldBe("validation.failed");
        var errors = problem.GetProperty("errors").GetProperty("spec").EnumerateArray().Select(e => e.GetString()).ToList();
        errors.Count.ShouldBe(1);
        errors[0]!.ShouldContain(spec["spec=".Length..]);
    }

    [Fact]
    public async Task GetProducts_UnknownSort_400()
    {
        var response = await Client().GetAsync("/api/catalog/products?sort=cheapest", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await Json(response);
        problem.GetProperty("type").GetString().ShouldBe("validation.failed");
        problem.GetProperty("errors").TryGetProperty("sort", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task GetProducts_PageSize100_400()
    {
        var response = await Client().GetAsync("/api/catalog/products?pageSize=100", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await Json(response);
        problem.GetProperty("type").GetString().ShouldBe("validation.failed");
        problem.GetProperty("errors").TryGetProperty("pageSize", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task GetProducts_UnknownCategory_404()
    {
        var response = await Client().GetAsync("/api/catalog/products?category=nope", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Json(response)).GetProperty("type").GetString().ShouldBe("catalog.category_not_found");
    }

    [Fact]
    public async Task GetProduct_Known_200_Detail()
    {
        var response = await Client().GetAsync("/api/catalog/products/arcwing-14", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var product = await Json(response);
        product.GetProperty("slug").GetString().ShouldBe("arcwing-14");
        product.GetProperty("variants").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task GetProduct_UnknownSlug_404_ProblemType()
    {
        var response = await Client().GetAsync("/api/catalog/products/does-not-exist", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Json(response)).GetProperty("type").GetString().ShouldBe("catalog.product_not_found");
    }

    [Fact]
    public async Task GetFacets_Known_200()
    {
        var response = await Client().GetAsync("/api/catalog/categories/laptops/facets", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(response)).GetProperty("slug").GetString().ShouldBe("laptops");
    }

    [Fact]
    public async Task GetFacets_UnknownCategory_404()
    {
        var response = await Client().GetAsync("/api/catalog/categories/nope/facets", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Json(response)).GetProperty("type").GetString().ShouldBe("catalog.category_not_found");
    }
}
