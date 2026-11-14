using Microsoft.Extensions.Logging;
using Shop.Application.Abstractions.Events;
using Shop.SharedKernel;

namespace Shop.Infrastructure.Common.Outbox;

/// <summary>
/// The single, scoped <see cref="IEventPublisher"/>: collects published integration events until the next
/// <see cref="Persistence.ModuleDbContext.SaveChangesAsync(bool, CancellationToken)"/> in this scope drains them into THAT
/// context's outbox. Invariant: one module per DI scope. Once a module context has drained events, a different module
/// context draining events in the same scope throws, because the events could otherwise land in the wrong module's outbox.
/// </summary>
public sealed class PendingIntegrationEvents(ILogger<PendingIntegrationEvents> logger) : IEventPublisher, IDisposable
{
    private readonly List<IIntegrationEvent> _pending = [];
    private Type? _owner;

    public void Publish(IIntegrationEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        _pending.Add(e);
    }

    /// <summary>Discards undrained events, e.g. after a failed save, so a retry in the same scope cannot drain them.</summary>
    internal void Clear() => _pending.Clear();

    public void Dispose()
    {
        if (_pending.Count > 0)
        {
            logger.LogWarning(
                "{Count} published integration event(s) were never saved to an outbox before the scope ended: {Types}",
                _pending.Count, string.Join(", ", _pending.Select(e => e.GetType().Name).Distinct()));
        }
    }

    internal IReadOnlyList<IIntegrationEvent> Drain(Type contextType)
    {
        if (_pending.Count == 0)
        {
            return [];
        }

        if (_owner is not null && _owner != contextType)
        {
            throw new InvalidOperationException(
                $"Integration events published in this scope were already routed to '{_owner.Name}'s outbox; " +
                $"'{contextType.Name}' cannot drain events in the same scope. Only one module may publish per DI scope.");
        }

        _owner = contextType;
        var drained = _pending.ToList();
        _pending.Clear();
        return drained;
    }
}
