using System.Text.Json;
using System.Text.Json.Serialization;

namespace Catalog.Infrastructure.Persistence.Converters;

/// <summary>
/// JSON settings for the catalog's JSONB columns (camelCase properties, enums as their names). The SQL read side must
/// deserialize these columns with the same options.
/// </summary>
public static class CatalogJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
