using System.Text.Json.Nodes;
using Catalog.Application.Products;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Shop.Api;

/// <summary>Tweaks to the generated OpenAPI document so the generated frontend types are precise.</summary>
internal static class OpenApiConfiguration
{
    public static void Configure(OpenApiOptions options)
    {
        options.AddSchemaTransformer((schema, _, _) =>
        {
            // .NET advertises numbers as number|string (it accepts quoted numbers); the contract is plain numbers.
            if (schema.Type is { } type && type.HasFlag(JsonSchemaType.String)
                && (type.HasFlag(JsonSchemaType.Number) || type.HasFlag(JsonSchemaType.Integer)))
            {
                schema.Type = type & ~JsonSchemaType.String;
                schema.Pattern = null;
            }

            // PagedResult.TotalPages is computed, so it is not a constructor argument, but it is always serialized.
            if (schema.Properties?.ContainsKey("totalPages") == true)
            {
                (schema.Required ??= new HashSet<string>()).Add("totalPages");
            }

            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            // A union type for sort in the generated client instead of a bare string.
            if (context.Description.ActionDescriptor.EndpointMetadata.OfType<EndpointNameMetadata>()
                    .Any(m => m.EndpointName == "SearchCatalogProducts")
                && operation.Parameters?.FirstOrDefault(p => p.Name == "sort")?.Schema is OpenApiSchema schema)
            {
                schema.Enum = [.. Enum.GetNames<ProductSort>().Select(n => (JsonNode)JsonValue.Create(char.ToLowerInvariant(n[0]) + n[1..])!)];
            }

            return Task.CompletedTask;
        });
    }
}
