using Microsoft.EntityFrameworkCore;
using Shop.Application.Abstractions.Events;
using Shop.Infrastructure.Common.Persistence;
using Shop.SharedKernel;

namespace Shop.Infrastructure.Common.Inbox;

/// <summary>
/// Makes an integration-event handler idempotent per (event Id, consumer). The inner handler's writes, any outbox rows it
/// publishes, and the inbox row commit in ONE transaction on <typeparamref name="TContext"/>, so a message is either fully
/// handled and recorded, or not at all. A concurrent duplicate loses on the inbox primary key and rolls back.
/// </summary>
public sealed class InboxHandler<TEvent, THandler, TContext>(THandler inner, TContext db, TimeProvider clock)
    : IIntegrationEventHandler<TEvent>
    where TEvent : IIntegrationEvent
    where THandler : class, IIntegrationEventHandler<TEvent>
    where TContext : ModuleDbContext
{
    private static readonly string Consumer = typeof(THandler).FullName!;

    public async Task Handle(TEvent e, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var alreadyHandled = await db.InboxMessages
            .AnyAsync(m => m.MessageId == e.Id && m.Consumer == Consumer, ct);
        if (alreadyHandled)
        {
            return;
        }

        await inner.Handle(e, ct);

        db.InboxMessages.Add(new InboxMessage
        {
            MessageId = e.Id,
            Consumer = Consumer,
            ProcessedOnUtc = clock.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
