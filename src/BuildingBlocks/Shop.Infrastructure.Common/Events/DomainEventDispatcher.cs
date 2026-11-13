using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions.Events;
using Shop.SharedKernel;

namespace Shop.Infrastructure.Common.Events;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken ct);
}

/// <summary>
/// Runs every <see cref="IDomainEventHandler{TEvent}"/> for each event's runtime type, resolved from the CURRENT scope
/// so handlers share the module DbContext (and therefore its transaction) with the code that raised the event.
/// Handlers run sequentially; the first exception propagates and aborts the SaveChanges that triggered dispatch.
/// </summary>
internal sealed class DomainEventDispatcher(IServiceProvider scopedProvider) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Func<object, IDomainEvent, CancellationToken, Task>> Invokers = new();

    private static readonly MethodInfo InvokeMethod =
        typeof(DomainEventDispatcher).GetMethod(nameof(Invoke), BindingFlags.NonPublic | BindingFlags.Static)!;

    public async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken ct)
    {
        foreach (var e in events)
        {
            var eventType = e.GetType();
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
            var invoke = Invokers.GetOrAdd(eventType, static t =>
                InvokeMethod.MakeGenericMethod(t).CreateDelegate<Func<object, IDomainEvent, CancellationToken, Task>>());

            foreach (var handler in scopedProvider.GetServices(handlerType))
            {
                await invoke(handler!, e, ct);
            }
        }
    }

    private static Task Invoke<TEvent>(object handler, IDomainEvent e, CancellationToken ct) where TEvent : IDomainEvent =>
        ((IDomainEventHandler<TEvent>)handler).Handle((TEvent)e, ct);
}
