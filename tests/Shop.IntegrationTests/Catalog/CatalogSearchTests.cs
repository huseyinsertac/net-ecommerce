using Catalog.Application.Categories;
using Catalog.Application.Products;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shop.Application.Abstractions;
using Shop.Application.Abstractions.Messaging;
using Shop.Application.Abstractions.Paging;
using Shop.SharedKernel;
using Shouldly;

namespace Shop.IntegrationTests.Catalog;

/// <summary>
/// Owns a fresh API + Postgres container seeded once with <see cref="CatalogTestData.SeedSearchDatasetAsync"/>, so the
/// search tests can assert exact global results without other tests' catalog rows getting in the way.
/// </summary>
public sealed class CatalogSearchFixture : IAsyncLifetime
{
    public ShopApiFactory Factory { get; } = new();

    public SearchDataset Data { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Factory.InitializeAsync();
        using (Factory.CreateClient())
        {
            // Forces host start (and the startup migration).
        }

        Data = await CatalogTestData.SeedSearchDatasetAsync(Factory.Services, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await Factory.DisposeAsync();
}

public sealed class CatalogSearchTests(CatalogSearchFixture fixture) : IClassFixture<CatalogSearchFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SearchProducts Search(
        string? category = null, string? text = null, IReadOnlyList<string>? brands = null, decimal? minPrice = null,
        decimal? maxPrice = null, bool inStockOnly = false, bool featuredOnly = false,
        IReadOnlyList<SpecFilter>? specs = null, ProductSort? sort = null, int page = 1, int pageSize = 48) =>
        new(category, text, brands ?? [], minPrice, maxPrice, inStockOnly, featuredOnly, specs ?? [], sort, page, pageSize);

    private static SpecFilter Range(string key, decimal? min, decimal? max) => new(key, min, max, []);

    private static SpecFilter Values(string key, params string[] values) => new(key, null, null, values);

    private async Task<Result<T>> Query<T>(IQuery<T> query)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().Query(query, Ct);
    }

    private async Task<PagedResult<ProductSummaryDto>> SearchOk(SearchProducts query)
    {
        var result = await Query(query);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? $"{result.Error.Code}: {result.Error.Message}" : null);
        return result.Value;
    }

    private static string[] Slugs(PagedResult<ProductSummaryDto> page) => [.. page.Items.Select(i => i.Slug)];

    private static string[] SpecErrors(Result result)
    {
        result.IsFailure.ShouldBeTrue();
        var error = result.Error.ShouldBeOfType<ValidationError>();
        error.Errors.ShouldContainKey("spec");
        return error.Errors["spec"];
    }

    [Fact]
    public async Task Search_NoFilters_ReturnsOnlyActive_FeaturedFirst()
    {
        var page = await SearchOk(Search());

        page.TotalCount.ShouldBe(10);
        Slugs(page).ShouldBe(fixture.Data.Active.Select(p => p.Slug), ignoreOrder: true);
        Slugs(page).ShouldNotContain(fixture.Data.Draft.Slug);
        Slugs(page).ShouldNotContain(fixture.Data.Archived.Slug);

        page.Items.Take(4).ShouldAllBe(i => i.IsFeatured);
        page.Items.Skip(4).ShouldAllBe(i => !i.IsFeatured);
        Slugs(page).Take(4).ShouldBe(["arcwing-14", "nimbus-book-15", "pulse-buds", "tessel-slate-12"]);

        var arcwing = page.Items.Single(i => i.Slug == "arcwing-14");
        arcwing.Name.ShouldBe("Arcwing 14 Laptop");
        arcwing.Brand.ShouldBe("Arcwing");
        arcwing.CategorySlug.ShouldBe("laptops");
        arcwing.ImageUrl.ShouldBe("/images/products/arcwing-14-front-400.webp");
        arcwing.PriceFrom.ShouldBe(1099.99m);
        arcwing.PriceTo.ShouldBe(1299.50m);
        arcwing.InStock.ShouldBeTrue();
        page.Items.Single(i => i.Slug == "nimbus-book-13").InStock.ShouldBeFalse();
        page.Items.Single(i => i.Slug == "nimbus-book-15").InStock.ShouldBeFalse();

        (await SearchOk(Search(featuredOnly: true))).TotalCount.ShouldBe(4);
    }

    [Fact]
    public async Task Search_PrefixText_FindsLaptops()
    {
        var laptops = await SearchOk(Search(text: "lapt"));
        Slugs(laptops).ShouldBe(
            ["arcwing-14", "arcwing-16-pro", "nimbus-book-13", "nimbus-book-15", "corvid-air"], ignoreOrder: true);

        var anc = await SearchOk(Search(text: "noise canc"));
        Slugs(anc).ShouldBe(["quietline-pro", "pulse-buds"], ignoreOrder: true);

        var quiet = await SearchOk(Search(text: "quietline", sort: ProductSort.Relevance));
        Slugs(quiet).ShouldBe(["quietline-lite", "quietline-pro"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("'; drop table products; --")]
    [InlineData("c++")]
    [InlineData("a:* | !b & (c)")]
    [InlineData("\\'\"%_")]
    public async Task Search_HostileText_NoErrorAndSaneResult(string text)
    {
        var page = await SearchOk(Search(text: text));
        page.Items.Count.ShouldBe(page.TotalCount);
        page.TotalCount.ShouldBeLessThanOrEqualTo(10);

        (await SearchOk(Search())).TotalCount.ShouldBe(10); // the table is still there
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("🔋💥")]
    [InlineData("...---...")]
    public async Task Search_TextWithoutWords_BehavesLikeNoSearch(string text)
    {
        var page = await SearchOk(Search(text: text));
        Slugs(page).ShouldBe(Slugs(await SearchOk(Search())));
    }

    [Theory]
    [InlineData("the")]
    [InlineData("and the")]
    [InlineData("with")]
    public async Task Search_StopWordsOnly_BehaveLikeNoText(string text)
    {
        var page = await SearchOk(Search(text: text)); // default sort: Relevance, which falls back to Featured order
        page.TotalCount.ShouldBe(10);
        Slugs(page).ShouldBe(Slugs(await SearchOk(Search())));
    }

    [Fact]
    public async Task Search_SingleLetterPrefix_MatchesFirstKeystroke()
    {
        // Lexemes starting with "a": arcwing, air, activ(e), adapt(ive). "a" itself is an English stop word.
        Slugs(await SearchOk(Search(text: "a")))
            .ShouldBe(["arcwing-14", "arcwing-16-pro", "corvid-air", "quietline-pro", "pulse-buds"], ignoreOrder: true);
        Slugs(await SearchOk(Search(text: "q"))).ShouldBe(["quietline-pro", "quietline-lite"], ignoreOrder: true);
    }

    [Fact]
    public async Task Search_StopWordBeforePrefix_IsIgnored()
    {
        Slugs(await SearchOk(Search(text: "the lapt")))
            .ShouldBe(["arcwing-14", "arcwing-16-pro", "nimbus-book-13", "nimbus-book-15", "corvid-air"], ignoreOrder: true);
        // A complete word whose English stem is shorter than the word still matches as the last token.
        Slugs(await SearchOk(Search(text: "headphones")))
            .ShouldBe(["quietline-pro", "quietline-lite"], ignoreOrder: true);
    }

    [Fact]
    public async Task Search_OversizedFilterLists_ValidationError()
    {
        var values = Enumerable.Range(0, 51).Select(i => $"v{i}").ToArray();
        var tooMany = await Query(Search(category: "laptops", specs: [Values("os", values)]));
        tooMany.Error.ShouldBeOfType<ValidationError>().Errors.ShouldContainKey("spec");

        var longValue = await Query(Search(category: "laptops", specs: [Values("os", new string('x', 101))]));
        longValue.Error.ShouldBeOfType<ValidationError>().Errors.ShouldContainKey("spec");

        var longBrand = await Query(Search(brands: [new string('b', 101)]));
        longBrand.Error.ShouldBeOfType<ValidationError>().Errors.ShouldContainKey("brands");

        var manyBrands = await Query(Search(brands: [.. Enumerable.Range(0, 21).Select(i => $"b{i}")]));
        manyBrands.Error.ShouldBeOfType<ValidationError>().Errors.ShouldContainKey("brands");

        // 50 values is the limit and still fine.
        (await Query(Search(category: "laptops", specs: [Values("os", [.. values.Take(49), "Linux"])]))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Search_TooLongText_ValidationError()
    {
        var result = await Query(Search(text: new string('a', 300)));
        result.Error.ShouldBeOfType<ValidationError>().Errors.ShouldContainKey(nameof(SearchProducts.Text));
    }

    [Fact]
    public async Task Search_ParentCategory_IncludesDescendants()
    {
        var page = await SearchOk(Search(category: "electronics"));
        Slugs(page).ShouldBe(
            ["arcwing-14", "arcwing-16-pro", "nimbus-book-13", "nimbus-book-15", "corvid-air", "tessel-slate-10", "tessel-slate-12"],
            ignoreOrder: true);

        Slugs(await SearchOk(Search(category: "tablets"))).ShouldBe(["tessel-slate-12", "tessel-slate-10"]);
    }

    [Fact]
    public async Task Search_BrandFilter_CaseInsensitive()
    {
        var page = await SearchOk(Search(brands: ["arcwing", "QUIETLINE"]));
        Slugs(page).ShouldBe(["arcwing-14", "arcwing-16-pro", "quietline-pro", "quietline-lite"], ignoreOrder: true);
    }

    [Fact]
    public async Task Search_PriceRange_MatchesAnyVariant()
    {
        var page = await SearchOk(Search(minPrice: 1200m, maxPrice: 1300m));
        var hit = page.Items.ShouldHaveSingleItem();
        hit.Slug.ShouldBe("arcwing-14");
        hit.PriceFrom.ShouldBe(1099.99m); // summary prices still span all variants
        hit.PriceTo.ShouldBe(1299.50m);

        Slugs(await SearchOk(Search(maxPrice: 130m))).ShouldBe(["pulse-buds", "quietline-lite"], ignoreOrder: true);
        Slugs(await SearchOk(Search(minPrice: 1800m))).ShouldBe(["arcwing-16-pro"]);
    }

    [Fact]
    public async Task Search_InStockOnly_ExcludesFullyReservedOrZeroStock()
    {
        var page = await SearchOk(Search(inStockOnly: true));
        page.TotalCount.ShouldBe(8);
        Slugs(page).ShouldNotContain("nimbus-book-13"); // zero stock
        Slugs(page).ShouldNotContain("nimbus-book-15"); // fully reserved
        Slugs(page).ShouldContain("pulse-buds");        // one of its variants is in stock
        page.Items.ShouldAllBe(i => i.InStock);

        // Price and stock must hold for the same variant: Pulse Buds' $129 variant is out of stock.
        Slugs(await SearchOk(Search(maxPrice: 135m, inStockOnly: true))).ShouldBe(["quietline-lite"]);
    }

    [Fact]
    public async Task Search_SpecNumberRange_And_EnumValues_And_Boolean()
    {
        Slugs(await SearchOk(Search(category: "laptops", specs: [Range("ram_gb", 16m, 64m)])))
            .ShouldBe(["arcwing-14", "arcwing-16-pro", "nimbus-book-15"], ignoreOrder: true);
        Slugs(await SearchOk(Search(category: "laptops", specs: [Range("ram_gb", null, 8m)])))
            .ShouldBe(["nimbus-book-13", "corvid-air"], ignoreOrder: true);

        Slugs(await SearchOk(Search(category: "laptops", specs: [Values("os", "Linux")])))
            .ShouldBe(["arcwing-16-pro", "nimbus-book-15", "corvid-air"], ignoreOrder: true);
        (await SearchOk(Search(category: "laptops", specs: [Values("os", "Windows 11", "Linux")]))).TotalCount.ShouldBe(5);

        Slugs(await SearchOk(Search(category: "laptops", specs: [Values("touchscreen", "true")])))
            .ShouldBe(["arcwing-14", "nimbus-book-15"], ignoreOrder: true);

        Slugs(await SearchOk(Search(category: "laptops",
                specs: [Range("ram_gb", 16m, null), Values("os", "Linux"), Values("touchscreen", "false")])))
            .ShouldBe(["arcwing-16-pro"]);

        Slugs(await SearchOk(Search(category: "tablets", specs: [Range("screen_in", 10.5m, 11m)])))
            .ShouldBe(["tessel-slate-10"]);
    }

    [Theory]
    [InlineData(ProductSort.PriceAsc)]
    [InlineData(ProductSort.PriceDesc)]
    [InlineData(ProductSort.Featured)]
    [InlineData(ProductSort.Newest)]
    public async Task Search_SortPriceAsc_StableAcrossPages(ProductSort sort)
    {
        var all = await SearchOk(Search(sort: sort, pageSize: 48));
        var paged = new List<string>();
        for (var page = 1; page <= 4; page++)
        {
            var result = await SearchOk(Search(sort: sort, page: page, pageSize: 3));
            result.TotalCount.ShouldBe(10);
            paged.AddRange(Slugs(result));
        }

        paged.ShouldBe(Slugs(all));
        paged.Distinct().Count().ShouldBe(10);

        if (sort == ProductSort.PriceAsc)
        {
            all.Items.Select(i => i.PriceFrom).ShouldBeInOrder(SortDirection.Ascending);
            // corvid-air and tessel-slate-12 both start at $549: the name tiebreak orders them.
            Slugs(all).ShouldBe(
            [
                "quietline-lite", "pulse-buds", "quietline-pro", "tessel-slate-10", "corvid-air", "tessel-slate-12",
                "nimbus-book-13", "nimbus-book-15", "arcwing-14", "arcwing-16-pro",
            ]);
        }
        else if (sort == ProductSort.PriceDesc)
        {
            all.Items.Select(i => i.PriceFrom).ShouldBeInOrder(SortDirection.Descending);
        }
        else if (sort == ProductSort.Newest)
        {
            Slugs(all).First().ShouldBe("corvid-air");
            Slugs(all).Last().ShouldBe("arcwing-14");
        }
    }

    [Fact]
    public async Task Search_PageBeyondEnd_EmptyItemsRealTotal()
    {
        var page = await SearchOk(Search(page: 5, pageSize: 24));
        page.Items.ShouldBeEmpty();
        page.TotalCount.ShouldBe(10);
        page.Page.ShouldBe(5);
        page.PageSize.ShouldBe(24);
    }

    [Theory]
    [InlineData(0, 24, "Page")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, 49, "PageSize")]
    public async Task Search_InvalidPaging_ValidationError(int page, int pageSize, string field)
    {
        var result = await Query(Search(page: page, pageSize: pageSize));
        result.Error.ShouldBeOfType<ValidationError>().Errors.ShouldContainKey(field);
    }

    [Fact]
    public async Task Search_InvalidPriceRange_ValidationError()
    {
        (await Query(Search(minPrice: 10m, maxPrice: 5m))).Error.ShouldBeOfType<ValidationError>();
        (await Query(Search(minPrice: -1m))).Error.ShouldBeOfType<ValidationError>();
    }

    [Fact]
    public async Task Search_SpecWithoutCategory_ValidationError()
    {
        SpecErrors(await Query(Search(specs: [Range("ram_gb", 8m, null)])))
            .ShouldBe(["Spec filters require a category."]);
        SpecErrors(await Query(Search(category: "electronics", specs: [Range("ram_gb", 8m, null)])))
            .ShouldBe(["Spec filters require a leaf category."]);
    }

    [Fact]
    public async Task Search_UnknownOrNonFilterableSpec_ValidationError()
    {
        SpecErrors(await Query(Search(category: "laptops", specs: [Values("gpu", "x")])))
            .ShouldBe(["Unknown or non-filterable spec 'gpu'."]);
        SpecErrors(await Query(Search(category: "laptops", specs: [Values("cpu", "Intel Core Ultra 7")])))
            .ShouldBe(["Unknown or non-filterable spec 'cpu'."]);
        SpecErrors(await Query(Search(category: "laptops", specs: [Values("ram_gb\" OR 1=1 --", "x")])))
            .ShouldBe(["Unknown or non-filterable spec 'ram_gb\" OR 1=1 --'."]);
    }

    [Fact]
    public async Task Search_RangeOnEnumSpec_ValidationError()
    {
        SpecErrors(await Query(Search(category: "laptops", specs: [Range("os", 1m, 2m)])))
            .ShouldBe(["Spec 'os' does not support ranges."]);
        SpecErrors(await Query(Search(category: "laptops", specs: [Values("ram_gb", "16")])))
            .ShouldBe(["Spec 'ram_gb' does not support values."]);
        SpecErrors(await Query(Search(category: "laptops", specs: [Values("touchscreen", "maybe")])))
            .ShouldBe(["Spec 'touchscreen' values must be true or false."]);
    }

    [Fact]
    public async Task Search_UnknownCategory_NotFound()
    {
        var result = await Query(Search(category: "no-such-category"));
        result.Error.Code.ShouldBe("catalog.category_not_found");
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Facets_LeafCategory_BrandsPriceAndSpecFacets_ExcludeDraft()
    {
        var facets = (await Query(new GetCategoryFacets("laptops"))).Value;

        facets.Slug.ShouldBe("laptops");
        facets.Name.ShouldBe("Laptops");
        facets.Brands.ShouldBe([new("Arcwing", 2), new("Corvid", 1), new("Nimbus", 2)]); // draft Arcwing X excluded
        facets.MinPrice.ShouldBe(549m);
        facets.MaxPrice.ShouldBe(1899m); // draft's $2999 excluded

        facets.Specs.Select(s => s.Key).ShouldBe(["ram_gb", "os", "touchscreen"]); // schema order; cpu not filterable
        var ram = facets.Specs[0];
        (ram.Label, ram.Type, ram.Unit, ram.Min, ram.Max).ShouldBe(("RAM", "number", "GB", 8m, 32m)); // draft's 64 excluded
        ram.Options.ShouldBeEmpty();

        var os = facets.Specs[1];
        os.Type.ShouldBe("enum");
        os.Options.ShouldBe([new("Windows 11", 2), new("Linux", 3), new("FreeBSD", 0)]); // schema order, zero counts kept

        var touch = facets.Specs[2];
        touch.Type.ShouldBe("boolean");
        touch.Options.ShouldBe([new("true", 2), new("false", 3)]);

        var parent = (await Query(new GetCategoryFacets("electronics"))).Value;
        parent.Specs.ShouldBeEmpty();
        parent.Brands.ShouldBe([new("Arcwing", 2), new("Corvid", 1), new("Nimbus", 2), new("Tessel", 2)]); // archived excluded
        parent.MinPrice.ShouldBe(399m);

        (await Query(new GetCategoryFacets("nope"))).Error.Code.ShouldBe("catalog.category_not_found");
    }

    [Fact]
    public async Task Facets_HeadphoneSpecs_BooleanCountsAndNumberRange()
    {
        var facets = (await Query(new GetCategoryFacets("headphones"))).Value;
        facets.Specs.Single(s => s.Key == "anc").Options.ShouldBe([new("true", 2), new("false", 1)]);
        var driver = facets.Specs.Single(s => s.Key == "driver_mm");
        (driver.Min, driver.Max).ShouldBe((12m, 40m));
    }

    [Fact]
    public async Task CategoryTree_CountsActiveOnly_InSortOrder()
    {
        var tree = (await Query(new GetCategoryTree())).Value;

        tree.Select(n => n.Slug).ShouldBe(["electronics", "audio"]);
        var electronics = tree[0];
        electronics.ProductCount.ShouldBe(7);
        electronics.ImageUrl.ShouldBe("/images/categories/electronics.webp");
        electronics.Icon.ShouldBe("devices");
        electronics.Children.Select(c => (c.Slug, c.ProductCount)).ShouldBe([("laptops", 5), ("tablets", 2)]);
        electronics.Children.ShouldAllBe(c => c.Children.Count == 0);

        var audio = tree[1];
        audio.ProductCount.ShouldBe(3);
        audio.Children.Select(c => (c.Slug, c.ProductCount)).ShouldBe([("headphones", 3)]);
    }

    [Fact]
    public async Task ProductDetail_Draft_NotFound()
    {
        var draft = await Query(new GetProductBySlug(fixture.Data.Draft.Slug));
        draft.Error.Code.ShouldBe("catalog.product_not_found");
        draft.Error.Type.ShouldBe(ErrorType.NotFound);

        (await Query(new GetProductBySlug(fixture.Data.Archived.Slug))).Error.Code.ShouldBe("catalog.product_not_found");
        (await Query(new GetProductBySlug("no-such-product"))).Error.Code.ShouldBe("catalog.product_not_found");
    }

    [Fact]
    public async Task ProductDetail_FormatsSpecsAndOrdersAxes()
    {
        var detail = (await Query(new GetProductBySlug("arcwing-14"))).Value;

        detail.Name.ShouldBe("Arcwing 14 Laptop");
        detail.Brand.ShouldBe("Arcwing");
        detail.Description.ShouldContain("laptop");
        detail.Breadcrumb.ShouldBe([new("electronics", "Electronics"), new("laptops", "Laptops")]);
        detail.Images.ShouldBe(
        [
            new("/images/products/arcwing-14-front.webp", "/images/products/arcwing-14-front-400.webp"),
            new("/images/products/arcwing-14-side.webp", "/images/products/arcwing-14-side-400.webp"),
        ]);
        detail.Specs.ShouldBe(
        [
            new("ram_gb", "RAM", "16 GB"),
            new("os", "Operating system", "Windows 11"),
            new("touchscreen", "Touchscreen", "Yes"),
            new("cpu", "Processor", "Intel Core Ultra 7"),
        ]);

        detail.Options.Select(o => (o.Key, o.Label)).ShouldBe([("color", "Color"), ("storage", "Storage")]);
        detail.Options[0].Values.ShouldBe(["Silver", "Black"]);
        detail.Options[1].Values.ShouldBe(["512 GB", "1 TB"]);

        detail.Variants.Select(v => v.Sku).ShouldBe(["AW14-SLV-512", "AW14-BLK-1TB"]);
        var silver = detail.Variants[0];
        silver.Price.ShouldBe(1099.99m);
        silver.Available.ShouldBe(5);
        silver.Options["color"].ShouldBe("Silver");

        var slate = (await Query(new GetProductBySlug("tessel-slate-10"))).Value;
        slate.Specs.ShouldBe([new("screen_in", "Screen size", "10.9 in")]);
        slate.Breadcrumb.Select(b => b.Slug).ShouldBe(["electronics", "tablets"]);

        var reserved = (await Query(new GetProductBySlug("nimbus-book-15"))).Value;
        reserved.Specs.Single(s => s.Key == "touchscreen").Value.ShouldBe("Yes");
        reserved.Variants.Single().Available.ShouldBe(0);
        (await Query(new GetProductBySlug("arcwing-16-pro"))).Value
            .Specs.Single(s => s.Key == "touchscreen").Value.ShouldBe("No");
    }

    [Fact]
    public async Task SeededDataset_IsTheOnlyCatalogData()
    {
        await using var connection = new NpgsqlConnection(fixture.Factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT count(*)::int FROM catalog.products", connection);
        ((int)(await command.ExecuteScalarAsync(Ct))!).ShouldBe(12);
    }
}
