using Shop.SharedKernel;

namespace Shop.Application.Abstractions.Events;

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task Handle(TEvent e, CancellationToken ct);
}

public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task Handle(TEvent e, CancellationToken ct);
}

/// <summary>Enlists the event in the current module outbox; it is persisted on SaveChanges.</summary>
public interface IEventPublisher
{
    void Publish(IIntegrationEvent e);
}
