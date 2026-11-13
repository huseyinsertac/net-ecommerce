namespace Shop.SharedKernel;

public interface IIntegrationEvent
{
    Guid Id { get; }
    DateTime OccurredOnUtc { get; }
}

public abstract record IntegrationEvent : IIntegrationEvent
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}
