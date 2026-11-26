using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Ordering.Infrastructure.Persistence;

/// <summary>JSON settings for the ordering jsonb columns: camelCase properties, enums as their names.</summary>
internal static class OrderingJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException($"A null {typeof(T).Name} was stored.");

    /// <summary>
    /// Compares by serialized JSON, so values without structural equality (lists) still compare correctly; the snapshot
    /// is a deep copy, so in-place changes to a tracked list are detected.
    /// </summary>
    public static ValueComparer<T> Comparer<T>() => new(
        (a, b) => Serialize(a) == Serialize(b),
        v => Serialize(v).GetHashCode(StringComparison.Ordinal),
        v => Deserialize<T>(Serialize(v)));
}
