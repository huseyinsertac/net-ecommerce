using Catalog.Domain.Reservations;
using Shouldly;

namespace Catalog.UnitTests.Reservations;

public sealed class StockReservationTests
{
    private static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    private static StockReservation New() =>
        StockReservation.Create(Guid.NewGuid(), [new ReservationLine(Guid.NewGuid(), Guid.NewGuid(), 2)], Now);

    [Fact]
    public void Commit_and_release_only_from_Reserved()
    {
        var committed = New();
        committed.TryCommit(Now.AddMinutes(1)).ShouldBeTrue();
        committed.Status.ShouldBe(ReservationStatus.Committed);
        committed.ClosedAtUtc.ShouldBe(Now.AddMinutes(1));
        committed.TryCommit(Now.AddMinutes(2)).ShouldBeFalse();
        committed.TryRelease(Now.AddMinutes(2)).ShouldBeFalse();
        committed.Status.ShouldBe(ReservationStatus.Committed);
        committed.ClosedAtUtc.ShouldBe(Now.AddMinutes(1));

        var released = New();
        released.TryRelease(Now).ShouldBeTrue();
        released.Status.ShouldBe(ReservationStatus.Released);
        released.TryRelease(Now).ShouldBeFalse();
        released.TryCommit(Now).ShouldBeFalse();
        released.Status.ShouldBe(ReservationStatus.Released);
    }

    [Fact]
    public void Create_starts_Reserved_with_order_id()
    {
        var orderId = Guid.NewGuid();
        var r = StockReservation.Create(orderId, [new ReservationLine(Guid.NewGuid(), Guid.NewGuid(), 1)], Now);
        r.Id.ShouldBe(orderId);
        r.Status.ShouldBe(ReservationStatus.Reserved);
        r.CreatedAtUtc.ShouldBe(Now);
        r.ClosedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void Create_rejects_empty_or_nonpositive_lines()
    {
        Should.Throw<ArgumentException>(() => StockReservation.Create(Guid.NewGuid(), [], Now));
        Should.Throw<ArgumentException>(() =>
            StockReservation.Create(Guid.NewGuid(), [new ReservationLine(Guid.NewGuid(), Guid.NewGuid(), 0)], Now));
        Should.Throw<ArgumentException>(() =>
            StockReservation.Create(Guid.NewGuid(), [new ReservationLine(Guid.NewGuid(), Guid.NewGuid(), -1)], Now));
    }
}
