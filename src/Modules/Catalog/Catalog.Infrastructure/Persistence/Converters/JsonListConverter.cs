using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Catalog.Infrastructure.Persistence.Converters;

/// <summary>Stores a read-only list as a JSON array (jsonb), round-tripping through a <see cref="List{T}"/>.</summary>
internal sealed class JsonListConverter<T>() : ValueConverter<IReadOnlyList<T>, string>(
    list => Serialize(list),
    json => Deserialize(json))
{
    /// <summary>
    /// Compares by serialized JSON, so element types without structural equality (records holding lists) still compare
    /// correctly; the snapshot is a deep copy.
    /// </summary>
    public static ValueComparer<IReadOnlyList<T>> Comparer { get; } = new(
        (a, b) => Serialize(a!) == Serialize(b!),
        list => Serialize(list).GetHashCode(StringComparison.Ordinal),
        list => Deserialize(Serialize(list)));

    private static string Serialize(IReadOnlyList<T> list) => JsonSerializer.Serialize(list, CatalogJson.Options);

    private static List<T> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<T>>(json, CatalogJson.Options) ?? [];
}

/// <summary>Stores a string-to-string map as a JSON object (jsonb), round-tripping through a <see cref="Dictionary{TKey,TValue}"/>.</summary>
internal sealed class JsonStringMapConverter() : ValueConverter<IReadOnlyDictionary<string, string>, string>(
    map => Serialize(map),
    json => Deserialize(json))
{
    public static ValueComparer<IReadOnlyDictionary<string, string>> Comparer { get; } = new(
        (a, b) => AreEqual(a!, b!),
        map => map.Aggregate(0, (hash, kv) => hash ^ HashCode.Combine(kv.Key, kv.Value)),
        map => new Dictionary<string, string>(map));

    private static bool AreEqual(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

    private static string Serialize(IReadOnlyDictionary<string, string> map) =>
        JsonSerializer.Serialize(map, CatalogJson.Options);

    private static Dictionary<string, string> Deserialize(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, CatalogJson.Options) ?? [];
}
