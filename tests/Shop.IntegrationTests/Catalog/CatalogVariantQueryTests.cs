using Catalog.Contracts.Variants;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Shop.IntegrationTests.Catalog;

[Collection(ShopApiCollection.Name)]
public sealed class CatalogVariantQueryTests(ShopApiFactory factory)
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

    private async Task<IReadOnlyDictionary<Guid, VariantSnapshot>> Get(params Guid[] ids)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICatalogVariantQuery>().GetAsync(ids, Ct);
    }

    private async Task Publish(Guid productId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var product = await db.Products.Include(p => p.Variants).SingleAsync(p => p.Id == productId, Ct);
        product.Publish().IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task GetAsync_ReturnsLivePriceAvailableAndThumbnail()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        await Publish(data.Laptop.Id);
        var variant = data.Laptop.Variants.First();

        var result = await Get(variant.Id);

        var snapshot = result[variant.Id];
        snapshot.ProductId.ShouldBe(data.Laptop.Id);
        snapshot.ProductSlug.ShouldBe(data.Laptop.Slug);
        snapshot.ProductName.ShouldBe(data.Laptop.Name);
        snapshot.Brand.ShouldBe("Arcwing");
        snapshot.Sku.ShouldBe(variant.Sku);
        snapshot.UnitPrice.ShouldBe(variant.Price.Amount);
        snapshot.Available.ShouldBe(5);
        snapshot.ThumbnailUrl.ShouldBe("/images/products/arcwing-14-front-400.webp");
        snapshot.IsPurchasable.ShouldBeTrue();
    }

    [Fact]
    public async Task GetAsync_OptionsLabel_InAxisOrder()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        // Axes are color then storage; the label follows that order, not alphabetical or dictionary order.
        var silver = data.Laptop.Variants.Single(v => v.Options["color"] == "Silver");

        var result = await Get(silver.Id, data.Headset.Variants.Single().Id);

        result[silver.Id].OptionsLabel.ShouldBe("Silver · 512 GB");
        result[data.Headset.Variants.Single().Id].OptionsLabel.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task GetAsync_UnknownIds_Omitted()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct);
        var known = data.Headset.Variants.Single().Id;
        var unknown = Guid.NewGuid();

        var result = await Get(known, unknown);

        result.ContainsKey(known).ShouldBeTrue();
        result.ContainsKey(unknown).ShouldBeFalse();
    }

    [Fact]
    public async Task GetAsync_DraftProduct_ReturnedNotPurchasable()
    {
        var data = await CatalogTestData.SeedAsync(Services, Ct); // products start as Draft
        var id = data.Laptop.Variants.First().Id;

        var result = await Get(id);

        result[id].IsPurchasable.ShouldBeFalse();
    }

    [Fact]
    public async Task GetAsync_EmptyInput_EmptyResult_NoQuery()
    {
        var result = await Get();

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAsync_MoreThan100Ids_Throws()
    {
        var ids = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();

        await Should.ThrowAsync<ArgumentException>(() => Get(ids));
    }
}
