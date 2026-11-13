using System.Text.Json;
using Shop.Application.Abstractions.Events;
using Shop.Infrastructure.Common.Events;
using Shop.Infrastructure.Common.Persistence;
using Shop.SharedKernel;

namespace Shop.Infrastructure.Common.Outbox;

/// <summary>Adds the event to the module's outbox; it is written by the caller's next SaveChangesAsync, atomically with its state.</summary>
public sealed class OutboxEventPublisher<TContext>(TContext db) : IEventPublisher where TContext : ModuleDbContext
{
    public void Publish(IIntegrationEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var type = e.GetType();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = e.Id,
            Type = type.FullName!,
            Content = JsonSerializer.Serialize(e, type, EventSerializer.Options),
            OccurredOnUtc = e.OccurredOnUtc,
        });
    }
}
