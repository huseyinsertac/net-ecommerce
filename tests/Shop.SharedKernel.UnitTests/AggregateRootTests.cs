using Shouldly;

namespace Shop.SharedKernel.UnitTests;

public class AggregateRootTests
{
    private sealed record TestEvent : DomainEvent;

    private sealed class TestAggregate : AggregateRoot<int>
    {
        public TestAggregate(int id) { Id = id; }
        public void DoTwoThings() { Raise(new TestEvent()); Raise(new TestEvent()); }
    }

    private sealed class TestEntity : Entity<int>
    {
        public TestEntity(int id) { Id = id; }
    }

    private sealed class OtherEntity : Entity<int>
    {
        public OtherEntity(int id) { Id = id; }
    }

    [Fact]
    public void Aggregate_RaiseAndClear()
    {
        var agg = new TestAggregate(1);
        agg.DoTwoThings();
        agg.DomainEvents.Count.ShouldBe(2);
        agg.ClearDomainEvents();
        agg.DomainEvents.Count.ShouldBe(0);
    }

    [Fact]
    public void Entities_SameIdSameType_AreEqual()
    {
        var a = new TestEntity(1);
        var b = new TestEntity(1);
        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.Equals(new OtherEntity(1)).ShouldBeFalse();
        a.Equals(new TestEntity(2)).ShouldBeFalse();
    }
}
