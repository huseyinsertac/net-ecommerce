using Catalog.Infrastructure.Reading;
using Shouldly;

namespace Catalog.UnitTests;

public sealed class TsQueryBuilderTests
{
    [Theory]
    [InlineData("noise canc", "noise & canc:*")]
    [InlineData("lapt", "lapt:*")]
    [InlineData("'; drop table x", "drop & table & x:*")]
    [InlineData("c++", "c:*")]
    [InlineData("   ", null)]
    [InlineData("🔋💥", null)]
    [InlineData("Wi-Fi 7 LAPTOP", "wi & fi & 7 & laptop:*")]
    [InlineData("a:* | !b & (c)", "a & b & c:*")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void Build_MapsTextToPrefixTsQuery(string? text, string? expected) =>
        TsQueryBuilder.Build(text).ShouldBe(expected);

    [Fact]
    public void Build_LongInput_TakesFirst8Tokens() =>
        TsQueryBuilder.Build("one two three four five six seven eight nine ten")
            .ShouldBe("one & two & three & four & five & six & seven & eight:*");
}
