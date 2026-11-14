using Catalog.Domain.Categories;
using Shop.SharedKernel;
using Shouldly;

namespace Catalog.UnitTests.Categories;

public class CategoryTests
{
    private static readonly SpecDefinition Ram = new("ram_gb", "RAM", SpecType.Number, "GB", true, []);
    private static readonly SpecDefinition Cpu = new("cpu", "CPU", SpecType.Text, null, false, []);
    private static readonly SpecDefinition Touch = new("touch", "Touchscreen", SpecType.Boolean, null, true, []);
    private static readonly SpecDefinition Os = new("os", "OS", SpecType.Enum, null, true, ["windows", "linux"]);

    private static Category NewCategory() =>
        Category.Create("laptops", "Laptops", "laptop", null, 1, [Ram, Cpu, Touch, Os]).Value;

    private static Dictionary<string, SpecValue> ValidValues() => new()
    {
        ["ram_gb"] = new NumberValue(16),
        ["cpu"] = new TextValue("Intel Core Ultra 7"),
        ["touch"] = new BoolValue(true),
        ["os"] = new TextValue("linux"),
    };

    [Fact]
    public void Create_ValidCategory_Succeeds()
    {
        var result = Category.Create("laptops", "Laptops", "laptop", null, 1, [Ram, Os]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldNotBe(Guid.Empty);
        result.Value.Slug.ShouldBe("laptops");
        result.Value.SpecSchema.Count.ShouldBe(2);
        result.Value.FindSpec("ram_gb").ShouldBe(Ram);
        result.Value.FindSpec("nope").ShouldBeNull();
    }

    [Theory]
    [InlineData("Laptops")]
    [InlineData("lap tops")]
    [InlineData("-x")]
    [InlineData("")]
    public void Create_BadSlug_Fails(string slug)
    {
        var result = Category.Create(slug, "Laptops", "laptop", null, 1, []);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.invalid_slug");
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Create_SlugTooLong_Fails()
    {
        Category.Create(new string('a', 81), "X", "i", null, 0, []).IsFailure.ShouldBeTrue();
        Category.Create(new string('a', 80), "X", "i", null, 0, []).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_DuplicateSpecKey_Fails()
    {
        var result = Category.Create("laptops", "Laptops", "laptop", null, 1, [Ram, Ram]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.invalid_spec_schema");
    }

    [Fact]
    public void Create_BadSpecKey_Fails()
    {
        var bad = Ram with { Key = "Ram GB" };
        Category.Create("laptops", "Laptops", "laptop", null, 1, [bad]).Error.Code
            .ShouldBe("catalog.invalid_spec_schema");
    }

    [Fact]
    public void Create_EnumWithoutOptions_Fails()
    {
        var result = Category.Create("laptops", "Laptops", "laptop", null, 1,
            [new SpecDefinition("os", "OS", SpecType.Enum, null, true, [])]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.invalid_spec_schema");
    }

    [Fact]
    public void Create_EnumOptionWithComma_Fails()
    {
        var result = Category.Create("laptops", "Laptops", "laptop", null, 1,
            [new SpecDefinition("os", "OS", SpecType.Enum, null, true, ["a,b"])]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.invalid_spec_schema");
    }

    [Fact]
    public void Create_NonEnumWithOptions_Fails()
    {
        var result = Category.Create("laptops", "Laptops", "laptop", null, 1,
            [new SpecDefinition("ram_gb", "RAM", SpecType.Number, null, true, ["x"])]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.invalid_spec_schema");
    }

    [Fact]
    public void ValidateSpecs_AllPresentAndTyped_Succeeds() =>
        NewCategory().ValidateSpecs(ValidValues()).IsSuccess.ShouldBeTrue();

    [Fact]
    public void ValidateSpecs_Missing_FailsWithCode()
    {
        var values = ValidValues();
        values.Remove("cpu");

        var result = NewCategory().ValidateSpecs(values);

        result.Error.Code.ShouldBe("catalog.spec_missing");
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Message.ShouldContain("cpu");
    }

    [Fact]
    public void ValidateSpecs_Unknown_FailsWithCode()
    {
        var values = ValidValues();
        values["weight"] = new NumberValue(1);

        var result = NewCategory().ValidateSpecs(values);

        result.Error.Code.ShouldBe("catalog.spec_unknown");
        result.Error.Message.ShouldContain("weight");
    }

    [Fact]
    public void ValidateSpecs_TypeMismatch_FailsWithCode()
    {
        var values = ValidValues();
        values["ram_gb"] = new TextValue("sixteen");

        var result = NewCategory().ValidateSpecs(values);

        result.Error.Code.ShouldBe("catalog.spec_type_mismatch");
        result.Error.Message.ShouldContain("ram_gb");
    }

    [Fact]
    public void ValidateSpecs_InvalidOption_FailsWithCode()
    {
        var values = ValidValues();
        values["os"] = new TextValue("beos");

        var result = NewCategory().ValidateSpecs(values);

        result.Error.Code.ShouldBe("catalog.spec_invalid_option");
        result.Error.Message.ShouldContain("os");
    }
}
