using Shop.SharedKernel;

namespace Catalog.Domain.Reservations;

public enum ReservationStatus
{
    Reserved,
    Committed,
    Released,
}

public sealed record ReservationLine(Guid ProductId, Guid VariantId, int Quantity);

/// <summary>The stock held for one order (<c>Id</c> is the order id): Reserved, then Committed or Released exactly once.</summary>
public sealed class StockReservation : AggregateRoot<Guid>
{
    private List<ReservationLine> _lines = [];

    private StockReservation() { }

    public IReadOnlyList<ReservationLine> Lines => _lines;
    public ReservationStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }

    public static StockReservation Create(Guid orderId, IEnumerable<ReservationLine> lines, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var copy = lines.ToList();
        if (copy.Count == 0)
        {
            throw new ArgumentException("A reservation needs at least one line.", nameof(lines));
        }

        if (copy.Any(l => l is null || l.Quantity <= 0))
        {
            throw new ArgumentException("Every reservation line needs a positive quantity.", nameof(lines));
        }

        return new StockReservation
        {
            Id = orderId,
            _lines = copy,
            Status = ReservationStatus.Reserved,
            CreatedAtUtc = nowUtc,
        };
    }

    /// <summary>Reserved to Committed; false (and no change) from any other status.</summary>
    public bool TryCommit(DateTime nowUtc) => Close(ReservationStatus.Committed, nowUtc);

    /// <summary>Reserved to Released; false (and no change) from any other status.</summary>
    public bool TryRelease(DateTime nowUtc) => Close(ReservationStatus.Released, nowUtc);

    private bool Close(ReservationStatus target, DateTime nowUtc)
    {
        if (Status != ReservationStatus.Reserved)
        {
            return false;
        }

        Status = target;
        ClosedAtUtc = nowUtc;
        return true;
    }
}
