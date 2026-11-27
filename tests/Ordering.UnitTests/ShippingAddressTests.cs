using Ordering.Domain.Orders;
using Shouldly;

namespace Ordering.UnitTests;

public class ShippingAddressTests
{
    [Fact]
    public void ShippingAddress_validates_and_normalizes_country()
    {
        var ok = ShippingAddress.Create(" Ada Lovelace ", " 1 Main St ", "  ", "London", " 12345 ", "us");
        ok.IsSuccess.ShouldBeTrue();
        ok.Value.Country.ShouldBe("US");
        ok.Value.FullName.ShouldBe("Ada Lovelace");
        ok.Value.Line2.ShouldBeNull();

        var bad = ShippingAddress.Create("Ada", "1 Main St", null, "London", "12345", "USA");
        bad.IsFailure.ShouldBeTrue();
        bad.Error.Code.ShouldBe("ordering.invalid_address");
    }

    [Theory]
    [InlineData("", "1 Main", "London", "123")]
    [InlineData("Ada", "", "London", "123")]
    [InlineData("Ada", "1 Main", "", "123")]
    [InlineData("Ada", "1 Main", "London", "")]
    [InlineData("Ada", "1 Main", "London", "123456789012345678901")]
    public void ShippingAddress_rejects_missing_or_too_long_fields(string name, string line1, string city, string postal)
    {
        ShippingAddress.Create(name, line1, null, city, postal, "US").IsFailure.ShouldBeTrue();
    }
}
