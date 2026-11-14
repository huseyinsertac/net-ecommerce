using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shop.SharedKernel;
using Shouldly;

namespace Shop.IntegrationTests.Catalog;

[Collection(ShopApiCollection.Name)]
public sealed class CatalogPersistenceTests(ShopApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Services
    {
        get
        {
            using var _ = factory.CreateClient(); // forces host start (and startup migration)
            return factory.Services;
        }
    }

    [Fact]
    public async Task Category_RoundTrips_WithSpecSchema()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var loaded = await db.Categories.SingleAsync(c => c.Id == data.Laptops.Id, Ct);

        loaded.Slug.ShouldBe(data.Laptops.Slug);
        loaded.Name.ShouldBe("Laptops");
        loaded.Icon.ShouldBe("devices");
        loaded.ParentId.ShouldBe(data.Electronics.Id);
        loaded.SortOrder.ShouldBe(1);
        loaded.SpecSchema.Count.ShouldBe(CatalogTestData.LaptopSchema.Count);
        for (var i = 0; i < loaded.SpecSchema.Count; i++)
        {
            var expected = CatalogTestData.LaptopSchema[i];
            var actual = loaded.SpecSchema[i];
            actual.Key.ShouldBe(expected.Key);
            actual.Label.ShouldBe(expected.Label);
            actual.Type.ShouldBe(expected.Type);
            actual.Unit.ShouldBe(expected.Unit);
            actual.Filterable.ShouldBe(expected.Filterable);
            actual.Options.ShouldBe(expected.Options);
        }

        var raw = await ScalarAsync<string>(
            "SELECT spec_schema::text FROM catalog.categories WHERE id = @id", ("id", data.Laptops.Id));
        raw.ShouldContain("\"key\": \"ram_gb\"");
        raw.ShouldContain("\"filterable\": true");
    }

    [Fact]
    public async Task Product_RoundTrips_SpecsVariantsAxesImages()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        var original = data.Laptop;

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var loaded = await db.Products.Include(p => p.Variants).SingleAsync(p => p.Id == original.Id, Ct);

        loaded.Slug.ShouldBe(original.Slug);
        loaded.Name.ShouldBe(original.Name);
        loaded.Brand.ShouldBe("Arcwing");
        loaded.Description.ShouldBe(original.Description);
        loaded.CategoryId.ShouldBe(data.Laptops.Id);
        loaded.Status.ShouldBe(ProductStatus.Draft);
        loaded.IsFeatured.ShouldBeTrue();
        loaded.CreatedAtUtc.ShouldBe(CatalogTestData.CreatedAtUtc);
        loaded.ImageKeys.ShouldBe(["arcwing-14-front", "arcwing-14-side"]);
        loaded.OptionAxes.ShouldBe([new OptionAxis("color", "Color"), new OptionAxis("storage", "Storage")]);

        loaded.Specs.Count.ShouldBe(4);
        loaded.Specs["ram_gb"].ShouldBe(new NumberValue(16m));
        loaded.Specs["os"].ShouldBe(new TextValue("Windows 11"));
        loaded.Specs["touchscreen"].ShouldBe(new BoolValue(true));
        loaded.Specs["cpu"].ShouldBe(new TextValue("Intel Core Ultra 7"));

        loaded.Variants.Count.ShouldBe(2);
        foreach (var expected in original.Variants)
        {
            var actual = loaded.Variants.Single(v => v.Id == expected.Id);
            actual.Sku.ShouldBe(expected.Sku);
            actual.Price.ShouldBe(expected.Price);
            actual.StockOnHand.ShouldBe(expected.StockOnHand);
            actual.StockReserved.ShouldBe(0);
            actual.Options.ShouldBe(expected.Options);
        }

        loaded.Variants.Single(v => v.Sku.StartsWith("AW14-BLK", StringComparison.Ordinal)).Price
            .ShouldBe(Money.Usd(1299.50m));
    }

    [Fact]
    public async Task Product_Changes_AreDetectedAndSaved()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        var variantId = data.Laptop.Variants[0].Id;

        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var product = await db.Products.Include(p => p.Variants).SingleAsync(p => p.Id == data.Laptop.Id, Ct);
            product.Publish().IsSuccess.ShouldBeTrue();
            product.ReserveStock(variantId, 2).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(Ct);
        }

        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var product = await db.Products.Include(p => p.Variants).SingleAsync(p => p.Id == data.Laptop.Id, Ct);
            product.Status.ShouldBe(ProductStatus.Active);
            product.Variants.Single(v => v.Id == variantId).StockReserved.ShouldBe(2);

            // An untouched aggregate must not be reported as modified (value comparers on the JSON columns).
            db.ChangeTracker.DetectChanges();
            db.ChangeTracker.HasChanges().ShouldBeFalse();
        }

        (await ScalarAsync<string>("SELECT status FROM catalog.products WHERE id = @id", ("id", data.Laptop.Id)))
            .ShouldBe("Active");
    }

    [Fact]
    public async Task SpecsStoredAsNativeJsonTypes()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);

        async Task<string> TypeOf(string key) => await ScalarAsync<string>(
            "SELECT jsonb_typeof(specs->@key) FROM catalog.products WHERE id = @id",
            ("key", key), ("id", data.Laptop.Id));

        (await TypeOf("ram_gb")).ShouldBe("number");
        (await TypeOf("touchscreen")).ShouldBe("boolean");
        (await TypeOf("os")).ShouldBe("string");
        (await TypeOf("cpu")).ShouldBe("string");
        (await ScalarAsync<decimal>(
            "SELECT (specs->>'ram_gb')::numeric FROM catalog.products WHERE id = @id", ("id", data.Laptop.Id)))
            .ShouldBe(16m);
    }

    [Fact]
    public async Task SearchVector_IsGeneratedFromNameBrandDescription()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);

        (await ScalarAsync<bool>(
            "SELECT search_vector @@ to_tsquery('english', 'arcwing & laptop') FROM catalog.products WHERE id = @id",
            ("id", data.Laptop.Id))).ShouldBeTrue();
    }

    [Fact]
    public async Task DuplicateSku_AcrossProducts_Rejected()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);

        // Same suffix → same SKUs as the already-saved laptop, but a different slug.
        var clash = CatalogTestData.Laptop(data.Laptops, data.Suffix, name: "Arcwing 14 Laptop Copy");

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Products.Add(clash);

        var ex = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        ex.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task ConcurrentReserveOfLastUnit_OneSucceeds_OtherGetsConcurrencyConflict()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        var variantId = data.Headset.Variants.Single().Id; // stock 1

        await using var scopeA = Services.CreateAsyncScope();
        await using var scopeB = Services.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var productA = await dbA.Products.Include(p => p.Variants).SingleAsync(p => p.Id == data.Headset.Id, Ct);
        var productB = await dbB.Products.Include(p => p.Variants).SingleAsync(p => p.Id == data.Headset.Id, Ct);

        productA.ReserveStock(variantId, 1).IsSuccess.ShouldBeTrue();
        productB.ReserveStock(variantId, 1).IsSuccess.ShouldBeTrue();

        await dbA.SaveChangesAsync(Ct);
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync(Ct));

        (await ScalarAsync<int>(
            "SELECT stock_reserved FROM catalog.product_variants WHERE id = @id", ("id", variantId))).ShouldBe(1);
    }

    [Fact]
    public async Task CheckConstraint_PreventsReservedAboveOnHand()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        var variantId = data.Headset.Variants.Single().Id;

        var ex = await Should.ThrowAsync<PostgresException>(() => ScalarAsync<int>(
            "UPDATE catalog.product_variants SET stock_reserved = stock_on_hand + 1 WHERE id = @id RETURNING 1",
            ("id", variantId)));

        ex.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        ex.ConstraintName.ShouldBe("ck_product_variants_stock");
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
