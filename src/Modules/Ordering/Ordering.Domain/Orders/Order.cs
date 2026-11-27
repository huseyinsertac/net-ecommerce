using Shop.SharedKernel;

namespace Ordering.Domain.Orders;

public sealed class Order : AggregateRoot<Guid>
{
    public const int MaxLines = 20;
    public const int MaxQuantityPerLine = 10;

    /// <summary>Every order number is this prefix plus the next value of the order-number sequence.</summary>
    public const string NumberPrefix = "VL-";

    private List<OrderLine> _lines = [];
    private List<StatusChange> _history = [];

    private Order() { }

    public string OrderNumber { get; private set; } = null!;
    public Guid CustomerId { get; private set; }
    public string CustomerEmail { get; private set; } = null!;
    public Guid IdempotencyKey { get; private set; }
    public ShippingAddress ShippingAddress { get; private set; } = null!;
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();
    public Money Subtotal { get; private set; } = null!;
    public Money Discount { get; private set; } = null!;
    public Money Total { get; private set; } = null!;
    public OrderStatus Status { get; private set; }
    public CancellationReason? CancellationReason { get; private set; }
    public bool RefundRequired { get; private set; }
    public DateTime PlacedAtUtc { get; private set; }
    public IReadOnlyList<StatusChange> History => _history.AsReadOnly();

    /// <summary>Seeded past order (demo accounts): it never went through the live reservation and payment flow.</summary>
    public bool IsHistorical { get; private set; }

    public static Result<Order> Place(
        string orderNumber, Guid customerId, string customerEmail, Guid idempotencyKey,
        ShippingAddress address, IReadOnlyCollection<NewOrderLine> lines, DateTime nowUtc)
    {
        var created = Create(orderNumber, customerId, customerEmail, idempotencyKey, address, lines, nowUtc, false);
        if (created.IsSuccess)
        {
            created.Value.Raise(new OrderPlacedDomainEvent(created.Value));
        }

        return created;
    }

    /// <summary>
    /// Like <see cref="Place"/> but flagged <see cref="IsHistorical"/> and raising no domain event, so no OrderPlaced is
    /// published and no stock is touched. The caller then applies the transitions with backdated times.
    /// </summary>
    public static Result<Order> PlaceHistorical(
        string orderNumber, Guid customerId, string customerEmail, Guid idempotencyKey,
        ShippingAddress address, IReadOnlyCollection<NewOrderLine> lines, DateTime nowUtc) =>
        Create(orderNumber, customerId, customerEmail, idempotencyKey, address, lines, nowUtc, true);

    private static Result<Order> Create(
        string orderNumber, Guid customerId, string customerEmail, Guid idempotencyKey,
        ShippingAddress address, IReadOnlyCollection<NewOrderLine> lines, DateTime nowUtc, bool historical)
    {
        if (lines.Count is < 1 or > MaxLines
            || lines.Any(l => l.Quantity is < 1 or > MaxQuantityPerLine)
            || lines.Select(l => l.VariantId).Distinct().Count() != lines.Count)
        {
            return OrderErrors.InvalidLines(MaxLines, MaxQuantityPerLine);
        }

        var orderLines = lines.Select((l, i) => OrderLine.Create(l, i + 1)).ToList();
        var subtotal = orderLines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);
        var discount = Money.Zero;

        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            OrderNumber = orderNumber,
            CustomerId = customerId,
            CustomerEmail = customerEmail,
            IdempotencyKey = idempotencyKey,
            ShippingAddress = address,
            _lines = orderLines,
            Subtotal = subtotal,
            Discount = discount,
            Total = Money.Usd(subtotal.Amount - discount.Amount),
            Status = OrderStatus.Placed,
            PlacedAtUtc = nowUtc,
            IsHistorical = historical,
        };
        order._history.Add(new StatusChange(OrderStatus.Placed, nowUtc, null));
        return order;
    }

    public Result MarkPaid(DateTime nowUtc) =>
        Transition(OrderStatus.Paid, nowUtc, null, OrderStatus.Placed);

    public Result Ship(DateTime nowUtc) =>
        Transition(OrderStatus.Shipped, nowUtc, null, OrderStatus.Paid);

    public Result Deliver(DateTime nowUtc) =>
        Transition(OrderStatus.Delivered, nowUtc, null, OrderStatus.Shipped);

    public Result Cancel(CancellationReason reason, DateTime nowUtc)
    {
        var wasPaid = Status == OrderStatus.Paid;
        var result = Transition(OrderStatus.Cancelled, nowUtc, reason.ToString(), OrderStatus.Placed, OrderStatus.Paid);
        if (result.IsFailure) return result;

        CancellationReason = reason;
        RefundRequired = wasPaid;
        return result;
    }

    private Result Transition(OrderStatus to, DateTime nowUtc, string? note, params OrderStatus[] allowedFrom)
    {
        if (!allowedFrom.Contains(Status))
        {
            return Result.Failure(OrderErrors.InvalidTransition(Status, to));
        }

        Status = to;
        _history.Add(new StatusChange(to, nowUtc, note));
        return Result.Success();
    }
}
