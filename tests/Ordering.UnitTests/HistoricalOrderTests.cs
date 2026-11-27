using Ordering.Domain.Orders;
using Shouldly;

namespace Ordering.UnitTests;

public class HistoricalOrderTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ShippingAddress Address() =>
        ShippingAddress.Create("Ada", "1 Main St", null, "London", "12345", "GB").Value;

    private static NewOrderLine Line() => new(Guid.NewGuid(), "Shoe", "SKU-1", "Red", null, 10m, 2);

    [Fact]
    public void PlaceHistorical_flags_the_order_and_raises_no_domain_event()
    {
        var order = Order.PlaceHistorical("VL-1", Guid.NewGuid(), "a@b.c", Guid.NewGuid(), Address(), [Line()], Now).Value;

        order.IsHistorical.ShouldBeTrue();
        order.DomainEvents.ShouldBeEmpty();
        order.Status.ShouldBe(OrderStatus.Placed);
        order.Total.Amount.ShouldBe(20m);
    }

    [Fact]
    public void PlaceHistorical_accepts_backdated_transitions()
    {
        var order = Order.PlaceHistorical("VL-1", Guid.NewGuid(), "a@b.c", Guid.NewGuid(), Address(), [Line()], Now).Value;

        order.MarkPaid(Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        order.Ship(Now.AddDays(1)).IsSuccess.ShouldBeTrue();
        order.Deliver(Now.AddDays(4)).IsSuccess.ShouldBeTrue();

        order.History.Select(h => h.Status).ShouldBe(
            [OrderStatus.Placed, OrderStatus.Paid, OrderStatus.Shipped, OrderStatus.Delivered]);
    }

    [Fact]
    public void PlaceHistorical_rejects_invalid_lines_like_Place()
    {
        Order.PlaceHistorical("VL-1", Guid.NewGuid(), "a@b.c", Guid.NewGuid(), Address(), [], Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Place_is_not_historical_and_raises_OrderPlaced()
    {
        var order = Order.Place("VL-1", Guid.NewGuid(), "a@b.c", Guid.NewGuid(), Address(), [Line()], Now).Value;

        order.IsHistorical.ShouldBeFalse();
        order.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<OrderPlacedDomainEvent>();
    }
}
