using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions.Events;
using Shop.SharedKernel;

namespace Shop.Infrastructure.Common.Events;

/// <summary>Delivers an integration event to its consumers. Called by the outbox processor, never by application code.</summary>
public interface IEventBus
{
    Task PublishAsync(IIntegrationEvent e, CancellationToken ct);
}

/// <summary>
/// Runs EVERY <see cref="IIntegrationEventHandler{TEvent}"/> registered for the
/// event's runtime type (in practice these are inbox-wrapped handlers), each in its OWN DI scope so handlers of different
/// modules never share a DbContext or a <see cref="Outbox.PendingIntegrationEvents"/> collector. All handlers run even if one fails; failures are
/// rethrown together as an <see cref="AggregateException"/> so the outbox retries the message. Handlers that already
/// succeeded are protected from re-running by their inbox rows.
/// </summary>
internal sealed class InProcessEventBus(IServiceScopeFactory scopeFactory) : IEventBus
{
    private static readonly ConcurrentDictionary<Type, Func<object, IIntegrationEvent, CancellationToken, Task>> Invokers = new();

    private static readonly MethodInfo InvokeMethod =
        typeof(InProcessEventBus).GetMethod(nameof(Invoke), BindingFlags.NonPublic | BindingFlags.Static)!;

    public async Task PublishAsync(IIntegrationEvent e, CancellationToken ct)
    {
        var eventType = e.GetType();
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        var invoke = Invokers.GetOrAdd(eventType, static t =>
            InvokeMethod.MakeGenericMethod(t).CreateDelegate<Func<object, IIntegrationEvent, CancellationToken, Task>>());

        // Count the registrations once, then resolve handler #i in a fresh scope.
        int handlerCount;
        await using (var probeScope = scopeFactory.CreateAsyncScope())
        {
            handlerCount = probeScope.ServiceProvider.GetServices(handlerType).Count();
        }

        List<Exception>? failures = null;
        for (var i = 0; i < handlerCount; i++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetServices(handlerType).ElementAt(i);
                await invoke(handler!, e, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException($"{failures.Count} handler(s) failed for {eventType.Name} {e.Id}.", failures);
        }
    }

    private static Task Invoke<TEvent>(object handler, IIntegrationEvent e, CancellationToken ct) where TEvent : IIntegrationEvent =>
        ((IIntegrationEventHandler<TEvent>)handler).Handle((TEvent)e, ct);
}
