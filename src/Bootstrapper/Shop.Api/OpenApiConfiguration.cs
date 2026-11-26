using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Shop.Api;

/// <summary>Tweaks to the generated OpenAPI document so the generated frontend types are precise.</summary>
internal static class OpenApiConfiguration
{
    private const string BearerScheme = "Bearer";

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

        // Access tokens are sent as "Authorization: Bearer <jwt>".
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Short-lived access token from the identity endpoints.",
            };
            return Task.CompletedTask;
        });

        // Only endpoints that require authorization (RequireAuthorization / [Authorize]) advertise the requirement.
        options.AddOperationTransformer((operation, context, _) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(BearerScheme, context.Document)] = [],
                });
            }

            return Task.CompletedTask;
        });
    }
}
