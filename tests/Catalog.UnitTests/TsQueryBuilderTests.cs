using Catalog.Infrastructure.Reading;
using Shouldly;

namespace Catalog.UnitTests;

public sealed class TsQueryBuilderTests
{
    [Theory]
    [InlineData("noise canc", "noise", "canc:*", false)]
    [InlineData("lapt", null, "lapt:*", false)]
    [InlineData("'; drop table x", "drop & table", "x:*", true)]
    [InlineData("c++", null, "c:*", true)]
    [InlineData("Wi-Fi 7 LAPTOP", "wi & fi & 7", "laptop:*", false)]
    [InlineData("a:* | !b & (c)", "a & b", "c:*", true)]
    [InlineData("the lapt", "the", "lapt:*", false)]
    [InlineData("the", null, "the:*", false)]
    [InlineData("ab", null, "ab:*", true)]
    public void Build_SplitsTextIntoWordsAndPrefix(string text, string? words, string prefix, bool shortPrefix) =>
        TsQueryBuilder.Build(text).ShouldBe(new TsQueryParts(words, prefix, shortPrefix));

    [Theory]
    [InlineData("   ")]
    [InlineData("🔋💥")]
    [InlineData("...---...")]
    [InlineData("")]
    [InlineData(null)]
    public void Build_NoWords_ReturnsNull(string? text) => TsQueryBuilder.Build(text).ShouldBeNull();

    [Fact]
    public void Build_LongInput_TakesFirst8Tokens() =>
        TsQueryBuilder.Build("one two three four five six seven eight nine ten")
            .ShouldBe(new TsQueryParts("one & two & three & four & five & six & seven", "eight:*", false));
}
