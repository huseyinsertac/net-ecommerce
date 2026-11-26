using Basket.Domain.Baskets;
using Shouldly;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.UnitTests;

public class BasketTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static BasketAggregate NewBasket() => BasketAggregate.Create(BasketOwner.ForAnonymous(Guid.NewGuid()), Now);

    [Fact]
    public void SetQuantity_NewVariant_AddsLine()
    {
        var basket = NewBasket();
        var variant = Guid.NewGuid();
        var later = Now.AddMinutes(1);

        var result = basket.SetQuantity(variant, 3, later);

        result.IsSuccess.ShouldBeTrue();
        basket.Lines.Count.ShouldBe(1);
        basket.Lines[0].VariantId.ShouldBe(variant);
        basket.Lines[0].Quantity.ShouldBe(3);
        basket.Lines[0].AddedAtUtc.ShouldBe(later);
        basket.UpdatedAtUtc.ShouldBe(later);
    }

    [Fact]
    public void SetQuantity_Existing_Replaces()
    {
        var basket = NewBasket();
        var variant = Guid.NewGuid();
        basket.SetQuantity(variant, 3, Now);

        basket.SetQuantity(variant, 5, Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();

        basket.Lines.Count.ShouldBe(1);
        basket.Lines[0].Quantity.ShouldBe(5);
    }

    [Fact]
    public void SetQuantity_Zero_Removes()
    {
        var basket = NewBasket();
        var variant = Guid.NewGuid();
        basket.SetQuantity(variant, 3, Now);

        basket.SetQuantity(variant, 0, Now).IsSuccess.ShouldBeTrue();

        basket.Lines.ShouldBeEmpty();
    }

    [Fact]
    public void SetQuantity_Eleven_InvalidQuantity()
    {
        var result = NewBasket().SetQuantity(Guid.NewGuid(), 11, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("basket.invalid_quantity");
    }

    [Fact]
    public void SetQuantity_Negative_InvalidQuantity()
    {
        var result = NewBasket().SetQuantity(Guid.NewGuid(), -1, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("basket.invalid_quantity");
    }

    [Fact]
    public void SetQuantity_TwentyFirstLine_TooManyLines()
    {
        var basket = NewBasket();
        for (var i = 0; i < BasketAggregate.MaxLines; i++)
        {
            basket.SetQuantity(Guid.NewGuid(), 1, Now).IsSuccess.ShouldBeTrue();
        }

        var result = basket.SetQuantity(Guid.NewGuid(), 1, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("basket.too_many_lines");
        basket.Lines.Count.ShouldBe(20);
    }

    [Fact]
    public void SetQuantity_ExistingLineWhenFull_Allowed()
    {
        var basket = NewBasket();
        var first = Guid.NewGuid();
        basket.SetQuantity(first, 1, Now);
        for (var i = 1; i < BasketAggregate.MaxLines; i++)
        {
            basket.SetQuantity(Guid.NewGuid(), 1, Now);
        }

        basket.SetQuantity(first, 4, Now).IsSuccess.ShouldBeTrue();

        basket.Lines.Single(l => l.VariantId == first).Quantity.ShouldBe(4);
    }

    [Fact]
    public void MergeFrom_SumsAndCaps()
    {
        var variant = Guid.NewGuid();
        var target = NewBasket();
        var source = NewBasket();
        target.SetQuantity(variant, 7, Now);
        source.SetQuantity(variant, 6, Now);

        target.MergeFrom(source, Now.AddMinutes(1));

        target.Lines.Single().Quantity.ShouldBe(10);
    }

    [Fact]
    public void MergeFrom_RespectsMaxLines()
    {
        var target = NewBasket();
        for (var i = 0; i < 19; i++)
        {
            target.SetQuantity(Guid.NewGuid(), 1, Now);
        }

        var source = NewBasket();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        source.SetQuantity(a, 2, Now);
        source.SetQuantity(b, 2, Now.AddSeconds(1));

        target.MergeFrom(source, Now.AddMinutes(1));

        target.Lines.Count.ShouldBe(20);
        target.Lines.ShouldContain(l => l.VariantId == a);
        target.Lines.ShouldNotContain(l => l.VariantId == b);
    }

    [Fact]
    public void MergeFrom_Self_Throws()
    {
        var basket = NewBasket();

        Should.Throw<InvalidOperationException>(() => basket.MergeFrom(basket, Now));
    }

    [Fact]
    public void Owner_ExactlyOneIdentifier()
    {
        var id = Guid.NewGuid();

        var user = BasketOwner.ForUser(id);
        user.UserId.ShouldBe(id);
        user.AnonymousId.ShouldBeNull();

        var anon = BasketOwner.ForAnonymous(id);
        anon.AnonymousId.ShouldBe(id);
        anon.UserId.ShouldBeNull();

        Should.Throw<ArgumentException>(() => BasketOwner.ForUser(Guid.Empty));
        Should.Throw<ArgumentException>(() => BasketOwner.ForAnonymous(Guid.Empty));
    }
}
