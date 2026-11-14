using System.Net.Http.Json;
using System.Text.Json;
using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Infrastructure.Common.Modules;
using Shouldly;

namespace Shop.IntegrationTests.Catalog;

/// <summary>Owns a fresh API + Postgres container whose startup seeding is switched on, so the seed runs against an empty catalog.</summary>
public sealed class CatalogSeedFixture : IAsyncLifetime
{
    public SeedingApiFactory Factory { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await Factory.InitializeAsync();
        using (Factory.CreateClient())
        {
            // Forces host start: migrations, then the startup seed.
        }
    }

    public async ValueTask DisposeAsync() => await Factory.DisposeAsync();

    public sealed class SeedingApiFactory : ShopApiFactory
    {
        protected override bool SeedOnStartup => true;
    }
}

public sealed class CatalogSeedTests(CatalogSeedFixture fixture) : IClassFixture<CatalogSeedFixture>
{
    private static readonly string[] LeafSlugs =
    [
        "phones", "laptops", "tablets", "headphones", "earbuds", "speakers",
        "wearables", "cameras", "gaming", "chargers-power", "cables-hubs",
    ];

    private static readonly string[] ParentSlugs = ["audio", "accessories"];

    private static readonly string[] RealBrandDenylist =
    [
        "Apple", "Samsung", "Sony", "Bose", "Dell", "Lenovo", "Google", "Xiaomi", "HP", "Asus", "Acer", "LG", "Huawei",
        "OnePlus", "JBL", "Logitech", "Anker", "Canon", "Nikon", "Fujifilm", "GoPro", "Garmin", "Fitbit", "Microsoft",
        "Nintendo", "Razer",
    ];

    // Lesser-known real electronics brands that a fictional brand must not collide with either.
    private static readonly string[] LesserKnownRealBrands =
    [
        "Nothing", "Realme", "Oppo", "Vivo", "Marshall", "Jabra", "Sennheiser", "Beats", "Skullcandy", "Ugreen", "Baseus",
        "Belkin", "Corsair", "SteelSeries", "HyperX", "Insta360", "DJI", "Polaroid", "Leica", "Kodak", "Ricoh", "GameSir",
        "8BitDo", "Amazfit", "Withings", "Suunto", "Pulsar", "Kestrel", "Nimbus",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(List<Category> Categories, List<Product> Products)> LoadAsync()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var categories = await db.Categories.AsNoTracking().ToListAsync(Ct);
        var products = await db.Products.AsNoTracking().Include(p => p.Variants).ToListAsync(Ct);
        return (categories, products);
    }

    [Fact]
    public async Task Seed_FreshDatabase_CreatesExpectedShape()
    {
        var (categories, products) = await LoadAsync();

        categories.Count.ShouldBe(13);
        categories.Select(c => c.Slug).ShouldBe([.. LeafSlugs, .. ParentSlugs], ignoreOrder: true);
        var parentIds = categories.Where(c => c.ParentId is not null).Select(c => c.ParentId!.Value).ToHashSet();
        var leaves = categories.Where(c => !parentIds.Contains(c.Id)).ToList();
        leaves.Count.ShouldBe(11);
        leaves.ShouldAllBe(c => c.SpecSchema.Count >= 4 && c.SpecSchema.Count <= 7);
        leaves.ShouldAllBe(c => c.SpecSchema.Count(s => s.Filterable) >= 3);
        leaves.ShouldAllBe(c => c.SpecSchema.Any(s => s.Filterable && s.Type == SpecType.Number));
        leaves.ShouldAllBe(c => c.SpecSchema.Any(s => s.Filterable && (s.Type == SpecType.Enum || s.Type == SpecType.Boolean)));
        categories.Where(c => parentIds.Contains(c.Id)).ShouldAllBe(c => c.SpecSchema.Count == 0);

        products.Count.ShouldBeInRange(75, 90);
        products.Count(p => p.IsFeatured).ShouldBe(8);
        products.Count(p => p.Status == ProductStatus.Draft).ShouldBe(2);
        products.Count(p => p.Status == ProductStatus.Active).ShouldBe(products.Count - 2);
        products.Where(p => p.Status == ProductStatus.Draft).ShouldAllBe(p => !p.IsFeatured);

        var leafIds = leaves.ToDictionary(c => c.Id, c => c.Slug);
        foreach (var group in products.GroupBy(p => leafIds[p.CategoryId]))
        {
            group.Count().ShouldBeInRange(6, 9, $"products in {group.Key}");
        }

        products.Select(p => p.Brand).Distinct().Count().ShouldBeInRange(8, 12);
        products.ShouldAllBe(p => p.OptionAxes.Count >= 1 && p.OptionAxes.Count <= 3);
        products.ShouldAllBe(p => p.Variants.Count >= 1 && p.Variants.Count <= 8);
        products.ShouldAllBe(p => p.ImageKeys.Count >= 1 && p.ImageKeys.Count <= 3);
        products.ShouldAllBe(p => p.Description.Length >= 80 && !p.Description.Contains("lorem", StringComparison.OrdinalIgnoreCase));

        var variants = products.SelectMany(p => p.Variants).ToList();
        variants.Count.ShouldBeInRange(220, 300);
        variants.Select(v => v.Sku).Distinct().Count().ShouldBe(variants.Count);
        variants.ShouldAllBe(v => v.Price.Currency == "USD" && (v.Price.Amount % 1m == 0m || v.Price.Amount % 1m == 0.99m));

        var total = (double)variants.Count;
        (variants.Count(v => v.StockOnHand == 0) / total).ShouldBeGreaterThanOrEqualTo(0.10);
        (variants.Count(v => v.StockOnHand is >= 1 and <= 5) / total).ShouldBeGreaterThanOrEqualTo(0.10);
        variants.ShouldAllBe(v => v.StockOnHand <= 120);
    }

    [Fact]
    public async Task Seed_RunTwice_IsNoOp()
    {
        var (categoriesBefore, productsBefore) = await LoadAsync();
        categoriesBefore.Count.ShouldBeGreaterThan(0);

        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var seeder = scope.ServiceProvider.GetServices<IModuleSeeder>().Single(s => s.Module == "catalog");
            await seeder.SeedAsync(Ct);
        }

        var (categoriesAfter, productsAfter) = await LoadAsync();
        categoriesAfter.Count.ShouldBe(categoriesBefore.Count);
        productsAfter.Count.ShouldBe(productsBefore.Count);
        productsAfter.Select(p => p.Id).ShouldBe(productsBefore.Select(p => p.Id), ignoreOrder: true);
    }

    [Fact]
    public async Task Seed_AllImageKeysExistInWebAssets()
    {
        var (categories, products) = await LoadAsync();
        var productImages = Path.Combine(RepoPaths.Root, "web", "shop-web", "public", "images", "products");
        var sources = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(RepoPaths.Root, "web", "shop-web", "scripts", "images", "sources.json"), Ct));
        var leafOfKey = sources.RootElement.EnumerateArray()
            .Where(e => e.GetProperty("kind").GetString() == "product")
            .ToDictionary(e => e.GetProperty("key").GetString()!, e => e.GetProperty("category").GetString()!);
        var slugOf = categories.ToDictionary(c => c.Id, c => c.Slug);

        var keys = products.SelectMany(p => p.ImageKeys).Distinct().ToList();
        keys.Count.ShouldBeGreaterThan(0);
        foreach (var key in keys)
        {
            File.Exists(Path.Combine(productImages, $"{key}.webp")).ShouldBeTrue($"{key}.webp is missing");
            File.Exists(Path.Combine(productImages, $"{key}-400.webp")).ShouldBeTrue($"{key}-400.webp is missing");
        }

        foreach (var product in products)
        {
            foreach (var key in product.ImageKeys)
            {
                leafOfKey.ShouldContainKey(key);
                leafOfKey[key].ShouldBe(slugOf[product.CategoryId], $"{product.Slug} uses a photo from another category: {key}");
            }
        }
    }

    [Fact]
    public async Task Seed_NoTwoProductsInALeafShareTheSameGallery()
    {
        var (_, products) = await LoadAsync();

        foreach (var group in products.GroupBy(p => p.CategoryId))
        {
            var galleries = group.Select(p => string.Join('|', p.ImageKeys)).ToList();
            galleries.Distinct().Count().ShouldBe(galleries.Count, "two products in one category have identical image lists");
        }

        products.SelectMany(p => p.ImageKeys).ShouldNotContain("phone-blank-01");
        products.SelectMany(p => p.ImageKeys).ShouldNotContain("controller-clear-04");
    }

    [Fact]
    public async Task Seed_AllCategoryImagesExist()
    {
        var (categories, _) = await LoadAsync();
        var dir = Path.Combine(RepoPaths.Root, "web", "shop-web", "public", "images", "categories");

        categories.ShouldNotBeEmpty();
        foreach (var category in categories)
        {
            File.Exists(Path.Combine(dir, $"{category.Slug}.webp")).ShouldBeTrue($"{category.Slug}.webp is missing");
            File.Exists(Path.Combine(dir, $"{category.Slug}-400.webp")).ShouldBeTrue($"{category.Slug}-400.webp is missing");
        }
    }

    [Fact]
    public async Task Seed_NoRealBrandNames()
    {
        var (_, products) = await LoadAsync();
        var brands = products.Select(p => p.Brand).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        brands.ShouldNotBeEmpty();
        brands.Intersect(RealBrandDenylist, StringComparer.OrdinalIgnoreCase).ShouldBeEmpty();
        brands.Intersect(LesserKnownRealBrands, StringComparer.OrdinalIgnoreCase).ShouldBeEmpty();
    }

    [Fact]
    public async Task Seed_EveryLeafHasSearchableFacets()
    {
        using var client = fixture.Factory.CreateClient();

        foreach (var slug in LeafSlugs)
        {
            var facets = await client.GetFromJsonAsync<JsonElement>($"/api/catalog/categories/{slug}/facets", Ct);

            var specsWithData = facets.GetProperty("specs").EnumerateArray().Count(spec =>
                spec.GetProperty("type").GetString() == "number"
                    ? spec.GetProperty("min").ValueKind == JsonValueKind.Number
                    : spec.GetProperty("options").EnumerateArray().Any(o => o.GetProperty("count").GetInt32() > 0));
            specsWithData.ShouldBeGreaterThanOrEqualTo(3, $"facets with data for {slug}");
            facets.GetProperty("brands").GetArrayLength().ShouldBeGreaterThan(0);
        }
    }
}
