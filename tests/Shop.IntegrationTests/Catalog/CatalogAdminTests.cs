using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Catalog.Application.Abstractions;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Contracts.Events;
using Shop.Infrastructure.Common.Events;
using Shop.IntegrationTests.Identity;
using Shouldly;

namespace Shop.IntegrationTests.Catalog;

/// <summary>The admin product API end to end, signed in as an Admin with a token from the host's own issuer.</summary>
[Collection(ShopApiCollection.Name)]
public sealed class CatalogAdminTests(ShopApiFactory factory)
{
    private const string Base = "/api/catalog/admin";

    private const string ConcurrentTitle = "Someone else changed this product. Reload to see their changes.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Services
    {
        get
        {
            using var _ = factory.CreateClient(); // forces host start (and startup migration)
            return factory.Services;
        }
    }

    // ---------- helpers ----------

    private HttpClient Admin() => Signed(User.CreateDemoAdmin(Email.Create($"admin-{Guid.NewGuid():N}@x.test").Value, DateTime.UtcNow));

    private HttpClient Signed(User user)
    {
        var token = Services.GetRequiredService<ITokenIssuer>().Issue(user, DateTime.UtcNow).Value;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task MutateProductAsync(Guid productId, Func<Product, Shop.SharedKernel.Result> change)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var product = await db.Products.Include(p => p.Variants).SingleAsync(p => p.Id == productId, Ct);
        change(product).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var raw = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(status, raw);
        return raw.Length == 0 ? default : JsonDocument.Parse(raw).RootElement.Clone();
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string type)
    {
        var problem = await JsonAsync(response, status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        problem.GetProperty("type").GetString().ShouldBe(type);
        return problem;
    }

    private static Task<JsonElement> GetProductAsync(HttpClient admin, Guid id) =>
        admin.GetAsync($"{Base}/products/{id}", Ct).ContinueWith(t => JsonAsync(t.Result), Ct).Unwrap();

    private static object Details(JsonElement product, string? name = null, string? status = null, uint? version = null) => new
    {
        name = name ?? product.GetProperty("name").GetString(),
        brand = product.GetProperty("brand").GetString(),
        description = product.GetProperty("description").GetString(),
        isFeatured = product.GetProperty("isFeatured").GetBoolean(),
        imageKeys = product.GetProperty("imageKeys"),
        specs = product.GetProperty("specs"),
        status = status ?? product.GetProperty("status").GetString(),
        version = version ?? product.GetProperty("version").GetUInt32(),
    };

    private static Task<HttpResponseMessage> PutDetailsAsync(HttpClient admin, Guid id, object body) =>
        admin.PutAsJsonAsync($"{Base}/products/{id}", body, Ct);

    private static Task<HttpResponseMessage> PutVariantAsync(
        HttpClient admin, Guid id, Guid variantId, decimal price, int stockOnHand, uint version) =>
        admin.PutAsJsonAsync($"{Base}/products/{id}/variants/{variantId}", new { price, stockOnHand, version }, Ct);

    private static object NewProduct(string categorySlug, string name, string skuSuffix, object? specs = null) => new
    {
        categorySlug,
        name,
        brand = "Testco",
        description = "A phone made for tests.",
        isFeatured = false,
        imageKeys = new[] { "arcwing-14-front" },
        specs = specs ?? new Dictionary<string, object> { ["ram_gb"] = 8, ["os"] = "Linux", ["touchscreen"] = false, ["cpu"] = "Test CPU" },
        optionAxes = new[] { new { name = "Color", values = new[] { "Black", "White" } } },
        variants = new[]
        {
            new { sku = $"TP-BLK-{skuSuffix}", options = new Dictionary<string, string> { ["Color"] = "Black" }, price = 199.99m, stockOnHand = 4 },
            new { sku = $"TP-WHT-{skuSuffix}", options = new Dictionary<string, string> { ["Color"] = "White" }, price = 209.99m, stockOnHand = 0 },
        },
    };

    private static Guid VariantId(JsonElement product, string skuPrefix) =>
        product.GetProperty("variants").EnumerateArray()
            .Single(v => v.GetProperty("sku").GetString()!.StartsWith(skuPrefix, StringComparison.Ordinal))
            .GetProperty("id").GetGuid();

    // ---------- tests ----------

    public static TheoryData<string, string> Routes => new()
    {
        { "GET", "/products" },
        { "GET", "/products/{id}" },
        { "POST", "/products" },
        { "PUT", "/products/{id}" },
        { "PUT", "/products/{id}/variants/{variantId}" },
        { "GET", "/image-keys" },
        { "GET", "/categories" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Customer_gets_403_and_anonymous_401_on_every_admin_route(string method, string route)
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        var path = Base + route
            .Replace("{id}", data.Laptop.Id.ToString(), StringComparison.Ordinal)
            .Replace("{variantId}", data.Laptop.Variants[0].Id.ToString(), StringComparison.Ordinal);
        HttpRequestMessage Request() => new(new HttpMethod(method), path)
        {
            Content = method == "GET" ? null : JsonContent.Create(new { }),
        };

        using var anonymous = factory.CreateClient();
        (await anonymous.SendAsync(Request(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var (customer, _, _) = await AuthTestClient.RegisterAndSignInAsync(factory);
        using (customer)
        {
            (await customer.SendAsync(Request(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using var admin = Admin();
        (await admin.SendAsync(Request(), Ct)).StatusCode.ShouldNotBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_includes_draft_and_archived_and_filters_by_status_and_query()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        await MutateProductAsync(data.Laptop.Id, p => p.Publish());
        await MutateProductAsync(data.Laptop.Id, p => p.Archive());
        using var admin = Admin();

        // The suffix only occurs in the SKUs (upper case), so this also proves SKU search is case-insensitive.
        var all = await JsonAsync(await admin.GetAsync($"{Base}/products?q={data.Suffix}", Ct));
        all.GetProperty("totalCount").GetInt32().ShouldBe(2);
        var items = all.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("name").GetString()).ShouldBe(["Arcwing 14 Laptop", "Quietline Pro Headphones"]);

        var laptop = items[0];
        laptop.GetProperty("id").GetGuid().ShouldBe(data.Laptop.Id);
        laptop.GetProperty("slug").GetString().ShouldBe(data.Laptop.Slug);
        laptop.GetProperty("brand").GetString().ShouldBe("Arcwing");
        laptop.GetProperty("categoryName").GetString().ShouldBe("Laptops");
        laptop.GetProperty("status").GetString().ShouldBe("Archived");
        laptop.GetProperty("isFeatured").GetBoolean().ShouldBeTrue();
        laptop.GetProperty("variantCount").GetInt32().ShouldBe(2);
        laptop.GetProperty("totalStock").GetInt32().ShouldBe(10);
        laptop.GetProperty("totalReserved").GetInt32().ShouldBe(0);
        laptop.GetProperty("minPrice").GetDecimal().ShouldBe(1099.99m);
        laptop.GetProperty("thumbnailUrl").GetString().ShouldBe("/images/products/arcwing-14-front-400.webp");
        items[1].GetProperty("status").GetString().ShouldBe("Draft");

        var archived = await JsonAsync(await admin.GetAsync($"{Base}/products?q={data.Suffix}&status=Archived", Ct));
        archived.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ShouldBe([data.Laptop.Id]);

        var drafts = await JsonAsync(await admin.GetAsync($"{Base}/products?q={data.Suffix}&status=Draft", Ct));
        drafts.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ShouldBe([data.Headset.Id]);

        var byName = await JsonAsync(await admin.GetAsync($"{Base}/products?q=QUIETLINE&categorySlug={data.Headphones.Slug}", Ct));
        byName.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ShouldBe([data.Headset.Id]);

        var paged = await JsonAsync(await admin.GetAsync($"{Base}/products?q={data.Suffix}&page=2&pageSize=1", Ct));
        paged.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ShouldBe([data.Headset.Id]);

        await ProblemAsync(await admin.GetAsync($"{Base}/products?status=Gone", Ct), HttpStatusCode.BadRequest, "validation.failed");
        await ProblemAsync(await admin.GetAsync($"{Base}/products?pageSize=500", Ct), HttpStatusCode.BadRequest, "validation.failed");
    }

    [Fact]
    public async Task Get_returns_the_editable_product_with_schema_and_version()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        using var admin = Admin();

        var product = await GetProductAsync(admin, data.Laptop.Id);

        product.GetProperty("slug").GetString().ShouldBe(data.Laptop.Slug);
        product.GetProperty("categoryId").GetGuid().ShouldBe(data.Laptops.Id);
        product.GetProperty("categorySlug").GetString().ShouldBe(data.Laptops.Slug);
        product.GetProperty("status").GetString().ShouldBe("Draft");
        product.GetProperty("imageKeys").EnumerateArray().Select(k => k.GetString()).ShouldBe(["arcwing-14-front", "arcwing-14-side"]);
        var specs = product.GetProperty("specs");
        specs.GetProperty("ram_gb").GetDecimal().ShouldBe(16m);
        specs.GetProperty("os").GetString().ShouldBe("Windows 11");
        specs.GetProperty("touchscreen").GetBoolean().ShouldBeTrue();
        var schema = product.GetProperty("specSchema").EnumerateArray().ToList();
        schema.Select(s => s.GetProperty("key").GetString()).ShouldBe(["ram_gb", "os", "touchscreen", "cpu"]);
        schema[1].GetProperty("type").GetString().ShouldBe("enum");
        schema[1].GetProperty("allowedValues").EnumerateArray().Select(v => v.GetString()).ShouldBe(["Windows 11", "Linux", "FreeBSD"]);
        schema[0].GetProperty("unit").GetString().ShouldBe("GB");
        schema[0].GetProperty("type").GetString().ShouldBe("number");
        schema[0].GetProperty("required").GetBoolean().ShouldBeTrue();
        product.GetProperty("optionAxes").EnumerateArray().Select(a => a.GetProperty("key").GetString()).ShouldBe(["color", "storage"]);
        var variant = product.GetProperty("variants").EnumerateArray().First();
        variant.GetProperty("stockOnHand").GetInt32().ShouldBe(5);
        variant.GetProperty("stockReserved").GetInt32().ShouldBe(0);
        product.GetProperty("version").GetUInt32().ShouldNotBe(0u);

        await ProblemAsync(await admin.GetAsync($"{Base}/products/{Guid.NewGuid()}", Ct), HttpStatusCode.NotFound,
            "catalog.product_not_found");
    }

    [Fact]
    public async Task Categories_and_image_keys_serve_the_editor()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        using var admin = Admin();

        var categories = (await JsonAsync(await admin.GetAsync($"{Base}/categories", Ct))).EnumerateArray().ToList();
        var slugs = categories.Select(c => c.GetProperty("slug").GetString()).ToList();
        slugs.ShouldContain(data.Laptops.Slug);
        slugs.ShouldContain(data.Headphones.Slug);
        slugs.ShouldNotContain(data.Electronics.Slug); // not a leaf
        var names = categories.Select(c => c.GetProperty("name").GetString()!).ToList();
        names.ShouldBe([.. names.Order(StringComparer.Ordinal)]);
        var headphones = categories.First(c => c.GetProperty("slug").GetString() == data.Headphones.Slug);
        headphones.GetProperty("specSchema").EnumerateArray().Select(s => s.GetProperty("type").GetString()).ShouldBe(["bool", "number"]);

        var keys = (await JsonAsync(await admin.GetAsync($"{Base}/image-keys", Ct))).EnumerateArray().ToList();
        var quietline = keys.Single(k => k.GetProperty("key").GetString() == "quietline-pro");
        quietline.GetProperty("thumbnailUrl").GetString().ShouldBe("/images/products/quietline-pro-400.webp");
        keys.Select(k => k.GetProperty("key").GetString()).ShouldBeUnique();
    }

    [Fact]
    public async Task Create_makes_a_draft_with_unique_slug()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        using var admin = Admin();

        var first = await admin.PostAsJsonAsync($"{Base}/products", NewProduct(data.Laptops.Slug, "Test Phone", $"A{data.Suffix}".ToUpperInvariant()), Ct);
        var second = await admin.PostAsJsonAsync($"{Base}/products", NewProduct(data.Laptops.Slug, "Test Phone", $"B{data.Suffix}".ToUpperInvariant()), Ct);

        var a = await JsonAsync(first, HttpStatusCode.Created);
        var b = await JsonAsync(second, HttpStatusCode.Created);
        a.GetProperty("slug").GetString().ShouldBe("test-phone");
        b.GetProperty("slug").GetString().ShouldBe("test-phone-2");
        var id = a.GetProperty("id").GetGuid();
        first.Headers.Location!.ToString().ShouldBe($"{Base}/products/{id}");

        var created = await GetProductAsync(admin, id);
        created.GetProperty("status").GetString().ShouldBe("Draft");
        created.GetProperty("name").GetString().ShouldBe("Test Phone");
        created.GetProperty("optionAxes").EnumerateArray().Single().GetProperty("label").GetString().ShouldBe("Color");
        var variants = created.GetProperty("variants").EnumerateArray().ToList();
        variants.Count.ShouldBe(2);
        variants.Single(v => v.GetProperty("sku").GetString() == $"TP-BLK-A{data.Suffix}".ToUpperInvariant())
            .GetProperty("price").GetDecimal().ShouldBe(199.99m);

        // A Draft is not public.
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/catalog/products/test-phone", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // SKUs are unique across products.
        var taken = await admin.PostAsJsonAsync($"{Base}/products", NewProduct(data.Laptops.Slug, "Test Phone", $"A{data.Suffix}".ToUpperInvariant()), Ct);
        await ProblemAsync(taken, HttpStatusCode.BadRequest, "catalog.duplicate_sku");
    }

    [Fact]
    public async Task Create_with_invalid_specs_is_400()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        using var admin = Admin();
        var suffix = data.Suffix.ToUpperInvariant();

        var wrongType = await admin.PostAsJsonAsync($"{Base}/products", NewProduct(data.Laptops.Slug, "Bad Specs", suffix,
            new Dictionary<string, object> { ["ram_gb"] = "lots", ["os"] = "Linux", ["touchscreen"] = false, ["cpu"] = "X" }), Ct);
        await ProblemAsync(wrongType, HttpStatusCode.BadRequest, "catalog.spec_type_mismatch");

        var missing = await admin.PostAsJsonAsync($"{Base}/products", NewProduct(data.Laptops.Slug, "Bad Specs", suffix,
            new Dictionary<string, object> { ["ram_gb"] = 8 }), Ct);
        await ProblemAsync(missing, HttpStatusCode.BadRequest, "catalog.spec_missing");

        var notAValue = await admin.PostAsJsonAsync($"{Base}/products", NewProduct(data.Laptops.Slug, "Bad Specs", suffix,
            new Dictionary<string, object> { ["ram_gb"] = new[] { 8 }, ["os"] = "Linux", ["touchscreen"] = false, ["cpu"] = "X" }), Ct);
        await ProblemAsync(notAValue, HttpStatusCode.BadRequest, "catalog.invalid_specs");

        var badPrice = await admin.PostAsJsonAsync($"{Base}/products", new
        {
            categorySlug = data.Laptops.Slug, name = "Bad Price", brand = "B", description = "D", isFeatured = false,
            imageKeys = new[] { "arcwing-14-front" },
            specs = new Dictionary<string, object> { ["ram_gb"] = 8, ["os"] = "Linux", ["touchscreen"] = false, ["cpu"] = "X" },
            optionAxes = Array.Empty<object>(),
            variants = new[] { new { sku = $"BP-{suffix}", options = new Dictionary<string, string>(), price = 1.999m, stockOnHand = 1 } },
        }, Ct);
        await ProblemAsync(badPrice, HttpStatusCode.BadRequest, "validation.failed");

        (await admin.GetAsync($"{Base}/products?q=Bad", Ct).ContinueWith(t => JsonAsync(t.Result), Ct).Unwrap())
            .GetProperty("items").EnumerateArray().ShouldNotContain(i => i.GetProperty("name").GetString()!.StartsWith("Bad", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_with_stale_version_is_409_concurrent_update_and_changes_nothing()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        using var admin = Admin();
        var original = await GetProductAsync(admin, data.Laptop.Id);
        var stale = original.GetProperty("version").GetUInt32();

        var saved = await JsonAsync(await PutDetailsAsync(admin, data.Laptop.Id, Details(original, name: "First Edit")));
        saved.GetProperty("name").GetString().ShouldBe("First Edit");
        saved.GetProperty("version").GetUInt32().ShouldNotBe(stale);

        var conflict = await ProblemAsync(
            await PutDetailsAsync(admin, data.Laptop.Id, Details(original, name: "Second Edit")),
            HttpStatusCode.Conflict, "catalog.concurrent_update");
        conflict.GetProperty("title").GetString().ShouldBe(ConcurrentTitle);
        (await GetProductAsync(admin, data.Laptop.Id)).GetProperty("name").GetString().ShouldBe("First Edit");

        // A variant edit changes the product's version too, so a details save based on the older read is refused.
        var beforeVariantEdit = await GetProductAsync(admin, data.Laptop.Id);
        var version = beforeVariantEdit.GetProperty("version").GetUInt32();
        var afterVariantEdit = await JsonAsync(await PutVariantAsync(
            admin, data.Laptop.Id, data.Laptop.Variants[0].Id, 1000m, 7, version));
        afterVariantEdit.GetProperty("version").GetUInt32().ShouldNotBe(version);

        await ProblemAsync(
            await PutDetailsAsync(admin, data.Laptop.Id, Details(beforeVariantEdit, name: "Third Edit")),
            HttpStatusCode.Conflict, "catalog.concurrent_update");
        await ProblemAsync(
            await PutVariantAsync(admin, data.Laptop.Id, data.Laptop.Variants[0].Id, 1m, 1, version),
            HttpStatusCode.Conflict, "catalog.concurrent_update");

        var final = await GetProductAsync(admin, data.Laptop.Id);
        final.GetProperty("name").GetString().ShouldBe("First Edit");
        var variant = final.GetProperty("variants").EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == data.Laptop.Variants[0].Id);
        variant.GetProperty("price").GetDecimal().ShouldBe(1000m);
        variant.GetProperty("stockOnHand").GetInt32().ShouldBe(7);

        await ProblemAsync(await PutDetailsAsync(admin, Guid.NewGuid(), Details(final)), HttpStatusCode.NotFound,
            "catalog.product_not_found");
    }

    [Fact]
    public async Task A_save_racing_another_admin_save_is_refused_at_write_time()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);

        // Admin A loads the product and passes the version check...
        await using var scopeA = Services.CreateAsyncScope();
        var repositoryA = scopeA.ServiceProvider.GetRequiredService<IProductRepository>();
        var productA = (await repositoryA.GetAsync(data.Laptop.Id, Ct))!;
        var versionA = repositoryA.VersionOf(productA);

        // ...then admin B saves a variant-only change first...
        await using (var scopeB = Services.CreateAsyncScope())
        {
            var repositoryB = scopeB.ServiceProvider.GetRequiredService<IProductRepository>();
            var productB = (await repositoryB.GetAsync(data.Laptop.Id, Ct))!;
            productB.SetVariantPrice(productB.Variants[0].Id, Shop.SharedKernel.Money.Usd(5m)).IsSuccess.ShouldBeTrue();
            (await repositoryB.SaveAsync(productB, repositoryB.VersionOf(productB), Ct)).ShouldBe(ProductSaveOutcome.Saved);
        }

        // ...so A's details-only save must not overwrite B's product row.
        productA.UpdateDetails("Racing Edit", "Arcwing", "Racing.", false, ["arcwing-14-front"]).IsSuccess.ShouldBeTrue();
        (await repositoryA.SaveAsync(productA, versionA, Ct)).ShouldBe(ProductSaveOutcome.ConcurrentUpdate);

        await using var check = Services.CreateAsyncScope();
        var saved = await check.ServiceProvider.GetRequiredService<CatalogDbContext>().Products.AsNoTracking()
            .Include(p => p.Variants).SingleAsync(p => p.Id == data.Laptop.Id, Ct);
        saved.Name.ShouldBe("Arcwing 14 Laptop");
        saved.Variants.Single(v => v.Id == productA.Variants[0].Id).Price.Amount.ShouldBe(5m);
    }

    [Fact]
    public async Task Update_validates_the_request()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        using var admin = Admin();
        var product = await GetProductAsync(admin, data.Laptop.Id);

        var noImages = await PutDetailsAsync(admin, data.Laptop.Id, new
        {
            name = "X", brand = "Y", description = "Z", isFeatured = false, imageKeys = Array.Empty<string>(),
            specs = product.GetProperty("specs"), status = "Draft", version = product.GetProperty("version").GetUInt32(),
        });
        await ProblemAsync(noImages, HttpStatusCode.BadRequest, "validation.failed");

        var noVersion = await PutDetailsAsync(admin, data.Laptop.Id, new
        {
            name = "X", brand = "Y", description = "Z", isFeatured = false, imageKeys = new[] { "arcwing-14-front" },
            specs = product.GetProperty("specs"), status = "Draft",
        });
        (await ProblemAsync(noVersion, HttpStatusCode.BadRequest, "validation.failed")).GetProperty("errors").TryGetProperty("version", out _).ShouldBeTrue();

        var unknownImage = await PutDetailsAsync(admin, data.Laptop.Id, new
        {
            name = "X", brand = "Y", description = "Z", isFeatured = false, imageKeys = new[] { "no-such-photo" },
            specs = product.GetProperty("specs"), status = "Draft", version = product.GetProperty("version").GetUInt32(),
        });
        await ProblemAsync(unknownImage, HttpStatusCode.BadRequest, "catalog.invalid_product");

        await ProblemAsync(
            await PutVariantAsync(admin, data.Laptop.Id, data.Laptop.Variants[0].Id, 0m, 1, product.GetProperty("version").GetUInt32()),
            HttpStatusCode.BadRequest, "validation.failed");
        await ProblemAsync(
            await PutVariantAsync(admin, data.Laptop.Id, Guid.NewGuid(), 10m, 1, product.GetProperty("version").GetUInt32()),
            HttpStatusCode.NotFound, "catalog.variant_not_found");

        (await GetProductAsync(admin, data.Laptop.Id)).GetProperty("version").GetUInt32()
            .ShouldBe(product.GetProperty("version").GetUInt32());
    }

    [Fact]
    public async Task Update_publishes_and_archives()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        using var admin = Admin();
        using var anonymous = factory.CreateClient();
        async Task<List<string?>> PublicSlugs() =>
            (await JsonAsync(await anonymous.GetAsync($"/api/catalog/products?category={data.Laptops.Slug}", Ct)))
            .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slug").GetString()).ToList();

        (await PublicSlugs()).ShouldBeEmpty();

        var draft = await GetProductAsync(admin, data.Laptop.Id);
        var published = await JsonAsync(await PutDetailsAsync(admin, data.Laptop.Id, Details(draft, status: "Active")));
        published.GetProperty("status").GetString().ShouldBe("Active");
        (await PublicSlugs()).ShouldBe([data.Laptop.Slug]);

        var archived = await JsonAsync(await PutDetailsAsync(admin, data.Laptop.Id, Details(published, status: "Archived")));
        archived.GetProperty("status").GetString().ShouldBe("Archived");
        (await PublicSlugs()).ShouldBeEmpty();
        (await anonymous.GetAsync($"/api/catalog/products/{data.Laptop.Slug}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await ProblemAsync(await PutDetailsAsync(admin, data.Laptop.Id, Details(archived, status: "Deleted")),
            HttpStatusCode.BadRequest, "validation.failed");
    }

    [Fact]
    public async Task Variant_stock_below_reserved_is_409()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        await MutateProductAsync(data.Laptop.Id, p => p.Publish());
        var variant = data.Laptop.Variants[0];
        using var admin = Admin();
        var version = (await GetProductAsync(admin, data.Laptop.Id)).GetProperty("version").GetUInt32();
        await Services.GetRequiredService<IEventBus>().PublishAsync(
            new OrderPlaced(Guid.NewGuid(), Guid.NewGuid(), [new OrderPlacedLine(variant.Id, 3)], 100m, "USD"), Ct);
        var product = await GetProductAsync(admin, data.Laptop.Id);
        // A shopper's reservation must not bump the product version the admin is editing against.
        product.GetProperty("version").GetUInt32().ShouldBe(version);
        product.GetProperty("variants").EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == variant.Id)
            .GetProperty("stockReserved").GetInt32().ShouldBe(3);

        var refused = await ProblemAsync(await PutVariantAsync(admin, data.Laptop.Id, variant.Id, 1099.99m, 2, version),
            HttpStatusCode.Conflict, "catalog.stock_below_reserved");
        refused.GetProperty("title").GetString()!.ShouldContain(variant.Sku);

        var saved = await JsonAsync(await PutVariantAsync(admin, data.Laptop.Id, variant.Id, 1099.99m, 3, version));
        saved.GetProperty("variants").EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == variant.Id)
            .GetProperty("stockOnHand").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task Version_still_bumps_on_a_variant_only_save_when_the_clock_does_not_move()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        var frozen = new FrozenClock(new DateTimeOffset(DateTime.UtcNow.Date.AddHours(DateTime.UtcNow.Hour), TimeSpan.Zero));
        await using var frozenFactory = factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<TimeProvider>(frozen)));
        var token = Services.GetRequiredService<ITokenIssuer>().Issue(
            User.CreateDemoAdmin(Email.Create($"admin-{Guid.NewGuid():N}@x.test").Value, DateTime.UtcNow), DateTime.UtcNow).Value;
        using var admin = frozenFactory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var variantId = data.Laptop.Variants[0].Id;
        var version = (await GetProductAsync(admin, data.Laptop.Id)).GetProperty("version").GetUInt32();

        var first = await JsonAsync(await PutVariantAsync(admin, data.Laptop.Id, variantId, 1001m, 5, version));
        var second = await JsonAsync(await PutVariantAsync(
            admin, data.Laptop.Id, variantId, 1002m, 5, first.GetProperty("version").GetUInt32()));

        second.GetProperty("version").GetUInt32().ShouldNotBe(first.GetProperty("version").GetUInt32());
    }

    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task Variant_price_change_shows_in_public_product_and_basket()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        await MutateProductAsync(data.Laptop.Id, p => p.Publish());
        var variant = data.Laptop.Variants[0];
        using var admin = Admin();
        var version = (await GetProductAsync(admin, data.Laptop.Id)).GetProperty("version").GetUInt32();

        await JsonAsync(await PutVariantAsync(admin, data.Laptop.Id, variant.Id, 899.49m, 5, version));

        using var anonymous = factory.CreateClient();
        var detail = await JsonAsync(await anonymous.GetAsync($"/api/catalog/products/{data.Laptop.Slug}", Ct));
        detail.GetProperty("variants").EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == variant.Id)
            .GetProperty("price").GetDecimal().ShouldBe(899.49m);

        var basket = await JsonAsync(await anonymous.PutAsJsonAsync($"/api/basket/items/{variant.Id}", new { quantity = 2 }, Ct));
        var line = basket.GetProperty("lines").EnumerateArray().Single();
        line.GetProperty("unitPrice").GetDecimal().ShouldBe(899.49m);
        basket.GetProperty("subtotal").GetDecimal().ShouldBe(1798.98m);
    }
}

/// <summary>The real shared demo admin, signed in through <c>/api/identity/demo/admin</c> on a seeded host.</summary>
public sealed class CatalogDemoAdminTests(DemoSeededApiFactory factory) : IClassFixture<DemoSeededApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Demo_admin_can_edit()
    {
        using var client = factory.CreateClient();
        var signIn = await client.PostAsync("/api/identity/demo/admin", null, Ct);
        signIn.StatusCode.ShouldBe(HttpStatusCode.OK);
        AuthTestClient.Bearer(client, await AuthTestClient.AccessTokenAsync(signIn));

        var list = JsonDocument.Parse(await client.GetStringAsync("/api/catalog/admin/products?pageSize=1", Ct)).RootElement;
        var id = list.GetProperty("items")[0].GetProperty("id").GetGuid();
        var product = JsonDocument.Parse(await client.GetStringAsync($"/api/catalog/admin/products/{id}", Ct)).RootElement;

        var response = await client.PutAsJsonAsync($"/api/catalog/admin/products/{id}", new
        {
            name = "Edited by the demo admin",
            brand = product.GetProperty("brand").GetString(),
            description = product.GetProperty("description").GetString(),
            isFeatured = product.GetProperty("isFeatured").GetBoolean(),
            imageKeys = product.GetProperty("imageKeys"),
            specs = product.GetProperty("specs"),
            status = product.GetProperty("status").GetString(),
            version = product.GetProperty("version").GetUInt32(),
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var saved = JsonDocument.Parse(await client.GetStringAsync($"/api/catalog/admin/products/{id}", Ct)).RootElement;
        saved.GetProperty("name").GetString().ShouldBe("Edited by the demo admin");
    }
}
