using Catalog.Infrastructure.Endpoints;
using Shouldly;

namespace Catalog.UnitTests;

public sealed class SpecFilterParserTests
{
    [Theory]
    [InlineData("ram_gb:16..64", "16", "64")]
    [InlineData("ram_gb:16..", "16", null)]
    [InlineData("ram_gb:..64", null, "64")]
    [InlineData("ram_gb:0.5..1.5", "0.5", "1.5")]
    public void TryParse_Range(string raw, string? min, string? max)
    {
        SpecFilterParser.TryParse(raw, out var filter, out var error).ShouldBeTrue(error);

        filter.Key.ShouldBe("ram_gb");
        filter.Min.ShouldBe(min is null ? null : decimal.Parse(min, System.Globalization.CultureInfo.InvariantCulture));
        filter.Max.ShouldBe(max is null ? null : decimal.Parse(max, System.Globalization.CultureInfo.InvariantCulture));
        filter.Values.ShouldBeEmpty();
    }

    [Fact]
    public void TryParse_ValueList_TrimsAndDropsEmptyEntries()
    {
        SpecFilterParser.TryParse("os: Windows 11 , ,Linux,", out var filter, out _).ShouldBeTrue();

        filter.Key.ShouldBe("os");
        filter.Values.ShouldBe(["Windows 11", "Linux"]);
        filter.Min.ShouldBeNull();
        filter.Max.ShouldBeNull();
    }

    [Fact]
    public void TryParse_Boolean_IsSingleValueList()
    {
        SpecFilterParser.TryParse("anc:true", out var filter, out _).ShouldBeTrue();

        filter.Key.ShouldBe("anc");
        filter.Values.ShouldBe(["true"]);
    }

    [Fact]
    public void TryParse_NegativeBounds()
    {
        SpecFilterParser.TryParse("temp_c:-5..-1", out var filter, out _).ShouldBeTrue();

        filter.Min.ShouldBe(-5m);
        filter.Max.ShouldBe(-1m);
    }

    [Fact]
    public void TryParse_ExtraColon_StaysInValue()
    {
        SpecFilterParser.TryParse("a:b:c", out var filter, out _).ShouldBeTrue();

        filter.Key.ShouldBe("a");
        filter.Values.ShouldBe(["b:c"]);
    }

    [Fact]
    public void TryParse_TrimsKey()
    {
        SpecFilterParser.TryParse("  ram_gb  :16..", out var filter, out _).ShouldBeTrue();

        filter.Key.ShouldBe("ram_gb");
    }

    [Theory]
    [InlineData("ram_gb")]
    [InlineData(":16")]
    [InlineData("ram_gb:..")]
    [InlineData("ram_gb:a..b")]
    [InlineData("ram_gb:")]
    [InlineData("ram_gb: , ")]
    [InlineData("ram_gb:1...5")]
    [InlineData("ram_gb:1....5")]
    [InlineData("")]
    public void TryParse_Malformed_ReturnsFalseWithError(string raw)
    {
        SpecFilterParser.TryParse(raw, out _, out var error).ShouldBeFalse();

        error.ShouldNotBeNullOrWhiteSpace();
    }
}
