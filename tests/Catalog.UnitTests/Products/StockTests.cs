using Shop.SharedKernel;
using Shouldly;

namespace Catalog.UnitTests.Products;

public class StockTests
{
    [Fact]
    public void Reserve_WithinAvailable_IncreasesReserved()
    {
        var p = ProductTests.Valid();
        var v = p.Variants[0];

        p.ReserveStock(v.Id, 3).IsSuccess.ShouldBeTrue();

        v.StockReserved.ShouldBe(3);
        v.Available.ShouldBe(2);
        v.StockOnHand.ShouldBe(5);
    }

    [Fact]
    public void Reserve_MoreThanAvailable_InsufficientStock()
    {
        var p = ProductTests.Valid();

        var result = p.ReserveStock(p.Variants[0].Id, 6);

        result.Error.Code.ShouldBe("catalog.insufficient_stock");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        p.Variants[0].StockReserved.ShouldBe(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reserve_NonPositiveQuantity_Invalid(int qty)
    {
        var p = ProductTests.Valid();

        var result = p.ReserveStock(p.Variants[0].Id, qty);

        result.Error.Code.ShouldBe("catalog.invalid_quantity");
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Reserve_UnknownVariant_NotFound()
    {
        var result = ProductTests.Valid().ReserveStock(Guid.NewGuid(), 1);

        result.Error.Code.ShouldBe("catalog.variant_not_found");
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public void Release_LowersReserved()
    {
        var p = ProductTests.Valid();
        var v = p.Variants[0];
        p.ReserveStock(v.Id, 3);

        p.ReleaseStock(v.Id, 2).IsSuccess.ShouldBeTrue();

        v.StockReserved.ShouldBe(1);
    }

    [Fact]
    public void Release_MoreThanReserved_Fails()
    {
        var p = ProductTests.Valid();
        var v = p.Variants[0];
        p.ReserveStock(v.Id, 1);

        var result = p.ReleaseStock(v.Id, 2);

        result.Error.Code.ShouldBe("catalog.release_exceeds_reserved");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void Commit_ReducesOnHandAndReserved()
    {
        var p = ProductTests.Valid();
        var v = p.Variants[0];
        p.ReserveStock(v.Id, 3);

        p.CommitStock(v.Id, 2).IsSuccess.ShouldBeTrue();

        v.StockOnHand.ShouldBe(3);
        v.StockReserved.ShouldBe(1);
    }

    [Fact]
    public void Commit_MoreThanReserved_Fails()
    {
        var p = ProductTests.Valid();
        var v = p.Variants[0];

        p.CommitStock(v.Id, 1).Error.Code.ShouldBe("catalog.release_exceeds_reserved");
        v.StockOnHand.ShouldBe(5);
    }
}
