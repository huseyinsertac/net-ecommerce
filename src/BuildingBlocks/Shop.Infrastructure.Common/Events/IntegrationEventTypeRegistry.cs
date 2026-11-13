using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Shop.SharedKernel;

namespace Shop.Infrastructure.Common.Events;

/// <summary>Maps the outbox <c>type</c> column (the event type's <c>FullName</c>) back to a CLR type.</summary>
public sealed class IntegrationEventTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _types = new(StringComparer.Ordinal);

    /// <summary>Registers every concrete <see cref="IIntegrationEvent"/> in a module's Contracts assembly.</summary>
    public void Register(Assembly contracts)
    {
        var eventTypes = contracts.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
                        && typeof(IIntegrationEvent).IsAssignableFrom(t));

        foreach (var type in eventTypes)
        {
            var existing = _types.GetOrAdd(type.FullName!, type);
            if (existing != type)
            {
                throw new InvalidOperationException(
                    $"Integration event name '{type.FullName}' is registered by both '{existing.Assembly.GetName().Name}' and '{type.Assembly.GetName().Name}'.");
            }
        }
    }

    public Type? Resolve(string typeName) => _types.GetValueOrDefault(typeName);
}

internal static class EventSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
