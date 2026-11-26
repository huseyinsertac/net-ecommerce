using Ordering.Domain.Orders;
using Shouldly;

namespace Ordering.UnitTests;

public class OrderTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ShippingAddress Address() =>
        ShippingAddress.Create("Ada", "1 Main St", null, "London", "12345", "GB").Value;

    private static NewOrderLine Line(Guid? variant = null, decimal price = 10m, int qty = 1) =>
        new(variant ?? Guid.NewGuid(), "Shoe", "SKU-1", "Red / 42", null, price, qty);

    private static Order NewOrder() =>
        Order.Place("VL-1", Guid.NewGuid(), "a@b.c", Guid.NewGuid(), Address(), [Line()], Now).Value;

    private static Order InStatus(OrderStatus status)
    {
        var o = NewOrder();
        switch (status)
        {
            case OrderStatus.Paid: o.MarkPaid(Now); break;
            case OrderStatus.Shipped: o.MarkPaid(Now); o.Ship(Now); break;
            case OrderStatus.Delivered: o.MarkPaid(Now); o.Ship(Now); o.Deliver(Now); break;
            case OrderStatus.Cancelled: o.Cancel(CancellationReason.Admin, Now); break;
        }
        return o;
    }

    [Fact]
    public void Place_computes_line_totals_subtotal_and_total()
    {
        var order = Order.Place("VL-1", Guid.NewGuid(), "a@b.c", Guid.NewGuid(), Address(),
            [Line(price: 199.99m, qty: 2), Line(price: 49.50m, qty: 1)], Now).Value;

        order.Lines[0].LineTotal.Amount.ShouldBe(399.98m);
        order.Lines[1].LineTotal.Amount.ShouldBe(49.50m);
        order.Subtotal.Amount.ShouldBe(449.48m);
        order.Discount.Amount.ShouldBe(0m);
        order.Total.Amount.ShouldBe(449.48m);
        order.Status.ShouldBe(OrderStatus.Placed);
        order.DomainEvents.OfType<OrderPlacedDomainEvent>().ShouldHaveSingleItem().Order.ShouldBe(order);
    }

    [Fact]
    public void Place_rejects_empty_duplicate_or_out_of_range_lines()
    {
        var v = Guid.NewGuid();
        Result(Array.Empty<NewOrderLine>()).IsFailure.ShouldBeTrue();
        Result([Line(v), Line(v)]).IsFailure.ShouldBeTrue();
        Result([Line(qty: 0)]).IsFailure.ShouldBeTrue();
        Result([Line(qty: 11)]).IsFailure.ShouldBeTrue();
        Result(Enumerable.Range(0, 21).Select(_ => Line()).ToArray()).IsFailure.ShouldBeTrue();
        Result(Enumerable.Range(0, 20).Select(_ => Line(qty: 10)).ToArray()).IsSuccess.ShouldBeTrue();
        Result([Line(qty: 0)]).Error.Code.ShouldBe("ordering.invalid_lines");

        static Shop.SharedKernel.Result<Order> Result(IReadOnlyCollection<NewOrderLine> lines) =>
            Order.Place("VL-1", Guid.NewGuid(), "a@b.c", Guid.NewGuid(), Address(), lines, Now);
    }

    public static TheoryData<OrderStatus, string, bool> Transitions()
    {
        var data = new TheoryData<OrderStatus, string, bool>();
        var allowed = new HashSet<(OrderStatus, string)>
        {
            (OrderStatus.Placed, "pay"), (OrderStatus.Placed, "cancel"),
            (OrderStatus.Paid, "ship"), (OrderStatus.Paid, "cancel"),
            (OrderStatus.Shipped, "deliver"),
        };
        foreach (var s in Enum.GetValues<OrderStatus>())
            foreach (var a in new[] { "pay", "ship", "deliver", "cancel" })
                data.Add(s, a, allowed.Contains((s, a)));
        return data;
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void Allowed_transitions_follow_the_state_machine(OrderStatus from, string action, bool allowed)
    {
        var order = InStatus(from);
        var result = action switch
        {
            "pay" => order.MarkPaid(Now),
            "ship" => order.Ship(Now),
            "deliver" => order.Deliver(Now),
            _ => order.Cancel(CancellationReason.Admin, Now),
        };

        result.IsSuccess.ShouldBe(allowed);
        if (!allowed)
        {
            result.Error.Code.ShouldBe("ordering.invalid_transition");
            result.Error.Type.ShouldBe(Shop.SharedKernel.ErrorType.Conflict);
            order.Status.ShouldBe(from);
        }
    }

    [Fact]
    public void Cancel_from_Paid_sets_RefundRequired()
    {
        var paid = InStatus(OrderStatus.Paid);
        paid.Cancel(CancellationReason.Admin, Now).IsSuccess.ShouldBeTrue();
        paid.RefundRequired.ShouldBeTrue();
        paid.CancellationReason.ShouldBe(CancellationReason.Admin);

        var placed = InStatus(OrderStatus.Placed);
        placed.Cancel(CancellationReason.OutOfStock, Now);
        placed.RefundRequired.ShouldBeFalse();
    }

    [Fact]
    public void History_records_each_transition_in_order()
    {
        var order = NewOrder();
        order.MarkPaid(Now.AddMinutes(1));
        order.Cancel(CancellationReason.Admin, Now.AddMinutes(2));

        order.History.Select(h => h.Status)
            .ShouldBe([OrderStatus.Placed, OrderStatus.Paid, OrderStatus.Cancelled]);
        order.History[0].AtUtc.ShouldBe(Now);
        order.History[2].AtUtc.ShouldBe(Now.AddMinutes(2));
        order.History[2].Note.ShouldBe("Admin");
        order.History[1].Note.ShouldBeNull();
    }
}
