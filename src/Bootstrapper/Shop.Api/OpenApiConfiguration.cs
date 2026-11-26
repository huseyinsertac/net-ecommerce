using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Shop.Infrastructure.Common.Http;

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

        // Every state-changing /api request must carry the CSRF header (see CsrfHeader); say so, so Scalar sends it.
        options.AddOperationTransformer((operation, context, _) =>
        {
            var method = context.Description.HttpMethod ?? HttpMethods.Get;
            var path = "/" + (context.Description.RelativePath ?? string.Empty).TrimStart('/');
            if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)
                || !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = CsrfHeader.Name,
                In = ParameterLocation.Header,
                Required = true,
                Description = $"Must be \"{CsrfHeader.Value}\". Without it the API answers 400 {CsrfHeader.ErrorCode}.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Default = CsrfHeader.Value },
            });
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
