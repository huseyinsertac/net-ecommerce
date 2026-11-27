using System.Text;
using System.Text.Json;
using Catalog.Domain.Categories;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Catalog.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores product specs as a JSON object with native JSON types (NumberValue → number, BoolValue → true/false,
/// TextValue → string), so SQL can filter them with <c>(specs->>'k')::numeric</c> and <c>::boolean</c>.
/// </summary>
public sealed class SpecValuesJsonConverter() : ValueConverter<IReadOnlyDictionary<string, SpecValue>, string>(
    specs => Serialize(specs),
    json => Deserialize(json))
{
    /// <summary>SpecValue subtypes are records, so values compare structurally.</summary>
    public static ValueComparer<IReadOnlyDictionary<string, SpecValue>> Comparer { get; } = new(
        (a, b) => AreEqual(a!, b!),
        specs => specs.Aggregate(0, (hash, kv) => hash ^ HashCode.Combine(kv.Key, kv.Value)),
        specs => new Dictionary<string, SpecValue>(specs));

    private static bool AreEqual(IReadOnlyDictionary<string, SpecValue> a, IReadOnlyDictionary<string, SpecValue> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && Equals(v, kv.Value));

    public static string Serialize(IReadOnlyDictionary<string, SpecValue> specs)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in specs)
            {
                switch (value)
                {
                    case NumberValue n:
                        writer.WriteNumber(key, n.Value);
                        break;
                    case BoolValue b:
                        writer.WriteBoolean(key, b.Value);
                        break;
                    case TextValue t:
                        writer.WriteString(key, t.Value);
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported spec value type '{value?.GetType().Name}' for '{key}'.");
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static Dictionary<string, SpecValue> Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        var specs = new Dictionary<string, SpecValue>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            specs[property.Name] = FromJson(property.Value)
                ?? throw new NotSupportedException($"Spec '{property.Name}' has unsupported JSON kind {property.Value.ValueKind}.");
        }

        return specs;
    }

    /// <summary>A JSON number, string or boolean as a spec value; null for any other JSON kind.</summary>
    public static SpecValue? FromJson(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetDecimal(out var number) => new NumberValue(number),
        JsonValueKind.True => new BoolValue(true),
        JsonValueKind.False => new BoolValue(false),
        JsonValueKind.String => new TextValue(value.GetString()!),
        _ => null,
    };

    /// <summary>The spec value as the JSON number, string or boolean it is stored as.</summary>
    public static JsonElement ToJson(SpecValue value) => value switch
    {
        NumberValue n => JsonSerializer.SerializeToElement(n.Value),
        BoolValue b => JsonSerializer.SerializeToElement(b.Value),
        TextValue t => JsonSerializer.SerializeToElement(t.Value),
        _ => throw new NotSupportedException($"Unsupported spec value type '{value?.GetType().Name}'."),
    };
}
