using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Shop.SharedKernel;
using Shouldly;

namespace Catalog.UnitTests.Products;

public class ProductTests
{
    internal static readonly SpecDefinition Ram = new("ram_gb", "RAM", SpecType.Number, "GB", true, []);

    internal static Category NewCategory() =>
        Category.Create("laptops", "Laptops", "laptop", null, 1, [Ram]).Value;

    internal static Dictionary<string, SpecValue> Specs() => new() { ["ram_gb"] = new NumberValue(16) };

    internal static NewVariant Variant(string sku, string color = "black", decimal price = 999m, int stock = 5) =>
        new(sku, new Dictionary<string, string> { ["color"] = color }, Money.Usd(price), stock);

    internal static Result<Product> Make(
        IReadOnlyDictionary<string, SpecValue>? specs = null,
        IReadOnlyList<string>? images = null,
        IReadOnlyList<NewVariant>? variants = null) =>
        Product.Create(NewCategory(), "volt-book-14", "VoltBook 14", "Voltix", "A laptop.",
            images ?? ["volt-book-14"], specs ?? Specs(), [new OptionAxis("color", "Color")],
            variants ?? [Variant("VB14-BLK")], true, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    internal static Product Valid() => Make().Value;

    [Fact]
    public void Create_Valid_StartsDraft()
    {
        var result = Make();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ProductStatus.Draft);
        result.Value.Variants.Count.ShouldBe(1);
        result.Value.Variants[0].Available.ShouldBe(5);
    }

    [Fact]
    public void Create_SpecsInvalid_PropagatesSpecError()
    {
        var result = Make(specs: new Dictionary<string, SpecValue>());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.spec_missing");
    }

    [Fact]
    public void Create_NullSpecValue_IsValidationError()
    {
        var result = Make(specs: new Dictionary<string, SpecValue> { ["ram_gb"] = null! });

        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Create_NoVariants_Fails() => Make(variants: []).IsFailure.ShouldBeTrue();

    [Fact]
    public void Create_VariantMissingAxis_Fails()
    {
        var bad = new NewVariant("VB14-BLK", new Dictionary<string, string>(), Money.Usd(10), 1);

        Make(variants: [bad]).Error.Code.ShouldBe("catalog.invalid_variant_options");
    }

    [Fact]
    public void Create_VariantExtraAxis_Fails()
    {
        var bad = new NewVariant("VB14-BLK",
            new Dictionary<string, string> { ["color"] = "black", ["size"] = "m" }, Money.Usd(10), 1);

        Make(variants: [bad]).Error.Code.ShouldBe("catalog.invalid_variant_options");
    }

    [Fact]
    public void Create_DuplicateOptionCombination_Fails() =>
        Make(variants: [Variant("A-1"), Variant("A-2")]).Error.Code.ShouldBe("catalog.invalid_variant_options");

    [Fact]
    public void Create_DuplicateSku_Fails() =>
        Make(variants: [Variant("A-1"), Variant("A-1", "white")]).Error.Code.ShouldBe("catalog.duplicate_sku");

    [Theory]
    [InlineData("")]
    [InlineData("lower")]
    public void Create_BadSku_Fails(string sku) =>
        Make(variants: [Variant(sku)]).Error.Code.ShouldBe("catalog.invalid_sku");

    [Fact]
    public void Create_ZeroPrice_Fails() =>
        Make(variants: [Variant("A-1", price: 0m)]).Error.Code.ShouldBe("catalog.invalid_price");

    [Fact]
    public void Create_NonUsdPrice_Fails()
    {
        var v = new NewVariant("A-1", new Dictionary<string, string> { ["color"] = "black" }, new Money(5m, "EUR"), 1);

        Make(variants: [v]).Error.Code.ShouldBe("catalog.invalid_price");
    }

    [Fact]
    public void Create_NegativeStock_Fails() =>
        Make(variants: [Variant("A-1", stock: -1)]).Error.Code.ShouldBe("catalog.invalid_stock");

    [Fact]
    public void Create_NoImages_Fails() => Make(images: []).IsFailure.ShouldBeTrue();

    [Fact]
    public void Create_NullInputs_ReturnValidationNotThrow()
    {
        var cat = NewCategory();
        var axes = new[] { new OptionAxis("color", "Color") };
        var v = new[] { Variant("A-1") };
        var now = DateTime.UtcNow;

        Product.Create(null!, "s", "n", "b", "d", ["i"], Specs(), axes, v, false, now).Error.Type.ShouldBe(ErrorType.Validation);
        Product.Create(cat, null!, "n", "b", "d", ["i"], Specs(), axes, v, false, now).Error.Type.ShouldBe(ErrorType.Validation);
        Product.Create(cat, "s", null!, "b", "d", ["i"], Specs(), axes, v, false, now).Error.Type.ShouldBe(ErrorType.Validation);
        Product.Create(cat, "s", "n", null!, "d", ["i"], Specs(), axes, v, false, now).Error.Type.ShouldBe(ErrorType.Validation);
        Product.Create(cat, "s", "n", "b", null!, ["i"], Specs(), axes, v, false, now).Error.Type.ShouldBe(ErrorType.Validation);
        Product.Create(cat, "s", "n", "b", "d", null!, Specs(), axes, v, false, now).Error.Type.ShouldBe(ErrorType.Validation);
        Product.Create(cat, "s", "n", "b", "d", ["i"], null!, axes, v, false, now).Error.Type.ShouldBe(ErrorType.Validation);
        Product.Create(cat, "s", "n", "b", "d", ["i"], Specs(), null!, v, false, now).Error.Type.ShouldBe(ErrorType.Validation);
        Product.Create(cat, "s", "n", "b", "d", ["i"], Specs(), axes, null!, false, now).Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Create_NameTooLong_Fails() =>
        Product.Create(NewCategory(), "s", new string('x', 201), "b", "d", ["i"], Specs(),
            [new OptionAxis("color", "Color")], [Variant("A-1")], false, DateTime.UtcNow).IsFailure.ShouldBeTrue();

    [Fact]
    public void Create_CopiesIncomingCollections()
    {
        var images = new List<string> { "a" };
        var specs = Specs();
        var opts = new Dictionary<string, string> { ["color"] = "black" };
        var product = Product.Create(NewCategory(), "s", "n", "b", "d", images, specs,
            [new OptionAxis("color", "Color")], [new NewVariant("A-1", opts, Money.Usd(5), 1)], false, DateTime.UtcNow).Value;

        images.Add("b");
        specs["extra"] = new TextValue("x");
        opts["color"] = "red";

        product.ImageKeys.Count.ShouldBe(1);
        product.Specs.Count.ShouldBe(1);
        product.Variants[0].Options["color"].ShouldBe("black");
    }

    [Fact]
    public void Publish_FromDraft_Active()
    {
        var product = Valid();

        product.Publish().IsSuccess.ShouldBeTrue();
        product.Status.ShouldBe(ProductStatus.Active);
    }

    [Fact]
    public void Publish_FromArchived_Active()
    {
        var product = Valid();
        product.Publish();
        product.Archive();

        product.Publish().IsSuccess.ShouldBeTrue();
        product.Status.ShouldBe(ProductStatus.Active);
    }

    [Fact]
    public void Publish_WhenActive_Fails()
    {
        var product = Valid();
        product.Publish();

        var result = product.Publish();

        result.Error.Code.ShouldBe("catalog.invalid_status_transition");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void Archive_FromDraft_Fails()
    {
        var result = Valid().Archive();

        result.Error.Code.ShouldBe("catalog.invalid_status_transition");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void Archive_FromActive_Archived()
    {
        var product = Valid();
        product.Publish();

        product.Archive().IsSuccess.ShouldBeTrue();
        product.Status.ShouldBe(ProductStatus.Archived);
    }
}
