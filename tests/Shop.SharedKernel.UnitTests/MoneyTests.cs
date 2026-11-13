using Shouldly;

namespace Shop.SharedKernel.UnitTests;

public class MoneyTests
{
    [Fact] public void Money_RoundsToTwoDecimals() => Money.Usd(10.005m).Amount.ShouldBe(10.01m);

    [Fact]
    public void Money_Negative_Throws() =>
        Should.Throw<ArgumentOutOfRangeException>(() => Money.Usd(-0.01m));

    [Fact] public void Money_Multiply() => (Money.Usd(19.99m) * 3).ShouldBe(Money.Usd(59.97m));

    [Fact] public void Money_EqualByValue() => Money.Usd(5m).ShouldBe(Money.Usd(5.00m));
}
