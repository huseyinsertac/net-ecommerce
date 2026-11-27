using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ordering.Infrastructure.Persistence;

/// <summary>Stores a value (a record or a read-only list) as a jsonb document, using <see cref="OrderingJson.Options"/>.</summary>
internal sealed class JsonColumnConverter<T>() : ValueConverter<T, string>(
    v => OrderingJson.Serialize(v),
    json => OrderingJson.Deserialize<T>(json));
