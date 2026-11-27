using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Shop.SharedKernel;
using Shouldly;

namespace Catalog.UnitTests.Products;

public class ProductAdminTests
{
    private static Product In(Category category) =>
        Product.Create(category, "volt-book-14", "VoltBook 14", "Voltix", "A laptop.", ["volt-book-14"],
            ProductTests.Specs(), [new OptionAxis("color", "Color")], [ProductTests.Variant("VB14-BLK")], true,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Value;

    [Fact]
    public void SetVariantStock_below_reserved_fails_with_sku_and_reserved()
    {
        var product = ProductTests.Valid();
        var variant = product.Variants[0];
        product.ReserveStock(variant.Id, 3).IsSuccess.ShouldBeTrue();

        var result = product.SetVariantStock(variant.Id, 2);

        result.Error.Code.ShouldBe("catalog.stock_below_reserved");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        result.Error.Message.ShouldContain("VB14-BLK");
        result.Error.Message.ShouldContain("3");
        variant.StockOnHand.ShouldBe(5);

        product.SetVariantStock(variant.Id, 3).IsSuccess.ShouldBeTrue();
        variant.StockOnHand.ShouldBe(3);
        variant.Available.ShouldBe(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100_001)]
    public void SetVariantStock_rejects_out_of_range(int stock)
    {
        var product = ProductTests.Valid();

        var result = product.SetVariantStock(product.Variants[0].Id, stock);

        result.Error.Code.ShouldBe("catalog.invalid_stock");
        product.Variants[0].StockOnHand.ShouldBe(5);
    }

    [Fact]
    public void SetVariantStock_accepts_the_bounds()
    {
        var product = ProductTests.Valid();

        product.SetVariantStock(product.Variants[0].Id, 0).IsSuccess.ShouldBeTrue();
        product.SetVariantStock(product.Variants[0].Id, 100_000).IsSuccess.ShouldBeTrue();
        product.Variants[0].StockOnHand.ShouldBe(100_000);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.001")]
    [InlineData("100000.01")]
    public void SetVariantPrice_rejects_out_of_range(string amount)
    {
        var product = ProductTests.Valid();

        var result = product.SetVariantPrice(product.Variants[0].Id, Money.Usd(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

        result.Error.Code.ShouldBe("catalog.invalid_price");
        result.Error.Type.ShouldBe(ErrorType.Validation);
        product.Variants[0].Price.ShouldBe(Money.Usd(999m));
    }

    [Fact]
    public void SetVariantPrice_accepts_the_bounds_and_needs_usd()
    {
        var product = ProductTests.Valid();
        var id = product.Variants[0].Id;

        product.SetVariantPrice(id, Money.Usd(0.01m)).IsSuccess.ShouldBeTrue();
        product.SetVariantPrice(id, Money.Usd(100_000m)).IsSuccess.ShouldBeTrue();
        product.Variants[0].Price.ShouldBe(Money.Usd(100_000m));
        product.SetVariantPrice(id, new Money(10m, "EUR")).Error.Code.ShouldBe("catalog.invalid_price");
    }

    [Fact]
    public void Variant_changes_on_an_unknown_variant_are_not_found()
    {
        var product = ProductTests.Valid();

        product.SetVariantPrice(Guid.NewGuid(), Money.Usd(10m)).Error.Code.ShouldBe("catalog.variant_not_found");
        product.SetVariantStock(Guid.NewGuid(), 1).Error.Code.ShouldBe("catalog.variant_not_found");
    }

    [Fact]
    public void UpdateDetails_requires_an_image()
    {
        var product = ProductTests.Valid();

        var result = product.UpdateDetails("New name", "Brand", "Description", false, []);

        result.Error.Code.ShouldBe("catalog.invalid_product");
        product.Name.ShouldBe("VoltBook 14");
        product.ImageKeys.ShouldBe(["volt-book-14"]);
    }

    [Fact]
    public void UpdateDetails_applies_the_same_rules_as_create()
    {
        var product = ProductTests.Valid();

        product.UpdateDetails(" ", "Brand", "Description", false, ["a"]).Error.Code.ShouldBe("catalog.invalid_product");
        product.UpdateDetails(new string('x', 201), "Brand", "Description", false, ["a"]).IsFailure.ShouldBeTrue();
        product.UpdateDetails("Name", " ", "Description", false, ["a"]).Error.Code.ShouldBe("catalog.invalid_name");
        product.UpdateDetails("Name", "Brand", " ", false, ["a"]).Error.Code.ShouldBe("catalog.invalid_name");
        product.UpdateDetails("Name", "Brand", "Description", false, ["a", " "]).Error.Code.ShouldBe("catalog.invalid_product");

        var ok = product.UpdateDetails("VoltBook 14 Pro", "Voltix", "A faster laptop.", false, ["b", "c"]);

        ok.IsSuccess.ShouldBeTrue();
        product.Name.ShouldBe("VoltBook 14 Pro");
        product.Brand.ShouldBe("Voltix");
        product.Description.ShouldBe("A faster laptop.");
        product.IsFeatured.ShouldBeFalse();
        product.ImageKeys.ShouldBe(["b", "c"]);
        product.Slug.ShouldBe("volt-book-14"); // the slug never changes, so links keep working
    }

    [Fact]
    public void SetSpecs_validates_against_category()
    {
        var category = ProductTests.NewCategory();
        var product = In(category);

        product.SetSpecs(category, new Dictionary<string, SpecValue>()).Error.Code.ShouldBe("catalog.spec_missing");
        product.SetSpecs(category, new Dictionary<string, SpecValue> { ["ram_gb"] = new TextValue("lots") })
            .Error.Code.ShouldBe("catalog.spec_type_mismatch");
        product.SetSpecs(category, new Dictionary<string, SpecValue>
        {
            ["ram_gb"] = new NumberValue(8),
            ["colour"] = new TextValue("red"),
        }).Error.Code.ShouldBe("catalog.spec_unknown");
        product.Specs["ram_gb"].ShouldBe(new NumberValue(16));

        product.SetSpecs(category, new Dictionary<string, SpecValue> { ["ram_gb"] = new NumberValue(32) }).IsSuccess.ShouldBeTrue();
        product.Specs["ram_gb"].ShouldBe(new NumberValue(32));
    }

    [Fact]
    public void SetSpecs_refuses_another_category()
    {
        var other = Category.Create("phones", "Phones", "phone", null, 1, [ProductTests.Ram]).Value;
        var product = ProductTests.Valid();

        product.SetSpecs(other, ProductTests.Specs()).Error.Code.ShouldBe("catalog.invalid_specs");
    }

    [Fact]
    public void SetStatus_moves_between_all_states()
    {
        var product = ProductTests.Valid();
        ProductStatus[] path =
        [
            ProductStatus.Active, ProductStatus.Draft, ProductStatus.Archived, ProductStatus.Active,
            ProductStatus.Archived, ProductStatus.Draft, ProductStatus.Draft,
        ];

        foreach (var status in path)
        {
            product.SetStatus(status).IsSuccess.ShouldBeTrue();
            product.Status.ShouldBe(status);
        }

        product.SetStatus((ProductStatus)42).Error.Code.ShouldBe("catalog.invalid_product");
        product.Status.ShouldBe(ProductStatus.Draft);
    }
}
