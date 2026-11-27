using System.ComponentModel;
using System.Text.Json;
using Catalog.Application.Admin;
using Catalog.Domain.Categories;
using Catalog.Infrastructure.Persistence.Converters;
using Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shop.Application.Abstractions.Messaging;
using Shop.Application.Abstractions.Paging;
using Shop.Infrastructure.Common.Http;
using Shop.SharedKernel;

namespace Catalog.Infrastructure.Endpoints;

/// <summary>
/// Product management for the (shared demo) admin. Every route needs the Admin policy. Writes carry the version they read
/// and are refused with 409 <c>catalog.concurrent_update</c> when someone else saved first. Nothing can be deleted.
/// </summary>
internal static class CatalogAdminEndpoints
{
    private const string Tag = "Catalog admin";

    public static void Map(RouteGroupBuilder group)
    {
        group.RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapGet("/products", ListProducts)
            .WithName("ListAdminProducts")
            .WithSummary("Products in every status, searched by name, brand or SKU, ordered by name.")
            .Produces<PagedResult<AdminProductSummaryDto>>()
            .ProducesValidationProblem()
            .WithAdminDefaults();

        group.MapGet("/products/{id:guid}", GetProduct)
            .WithName("GetAdminProduct")
            .WithSummary("One product as the editor needs it: raw spec values, the category's spec schema, stock and the version.")
            .Produces<AdminProductDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithAdminDefaults();

        group.MapPost("/products", CreateAsync)
            .WithName("CreateAdminProduct")
            .WithSummary("Creates a Draft product. Its slug comes from the name; a taken slug gets -2, -3 and so on.")
            .Produces<CreatedProductDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithAdminDefaults();

        group.MapPut("/products/{id:guid}", UpdateAsync)
            .WithName("UpdateAdminProduct")
            .WithSummary("Saves a product's details, specs and status (Draft, Active or Archived). Returns the product with its new version.")
            .Produces<AdminProductDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithAdminDefaults();

        group.MapPut("/products/{id:guid}/variants/{variantId:guid}", UpdateVariantAsync)
            .WithName("UpdateAdminVariant")
            .WithSummary("Sets a variant's price and stock on hand (never below its reserved stock). Returns the product with its new version.")
            .Produces<AdminProductDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithAdminDefaults();

        group.MapGet("/image-keys", GetImageKeys)
            .WithName("ListAdminImageKeys")
            .WithSummary("Every product photo in use, to pick images from.")
            .Produces<IReadOnlyList<AdminImageKeyDto>>()
            .WithAdminDefaults();

        group.MapGet("/categories", GetCategories)
            .WithName("ListAdminCategories")
            .WithSummary("Leaf categories (the ones products belong to) with their full spec schema, ordered by name.")
            .Produces<IReadOnlyList<AdminCategoryDto>>()
            .WithAdminDefaults();
    }

    private static RouteHandlerBuilder WithAdminDefaults(this RouteHandlerBuilder builder) => builder
        .WithTags(Tag)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

    /// <param name="Specs">Spec key to value: a JSON number, string or boolean, exactly as the admin product returns them.</param>
    private sealed record CreateProductRequest(
        string? CategorySlug,
        string? Name,
        string? Brand,
        string? Description,
        bool IsFeatured,
        IReadOnlyList<string>? ImageKeys,
        Dictionary<string, JsonElement>? Specs,
        IReadOnlyList<NewOptionAxisInput>? OptionAxes,
        IReadOnlyList<NewVariantInput>? Variants);

    private sealed record UpdateProductRequest(
        string? Name,
        string? Brand,
        string? Description,
        bool IsFeatured,
        IReadOnlyList<string>? ImageKeys,
        [property: Description("Spec key to value: a JSON number, string or boolean, exactly as the admin product returns them.")]
        Dictionary<string, JsonElement>? Specs,
        [property: Description("Draft, Active or Archived. Only Active products are public.")] string? Status,
        [property: Description("The version from the admin product this edit is based on.")] uint? Version);

    private sealed record UpdateVariantRequest(
        [property: Description("USD, 0.01 to 100000.00, at most 2 decimals.")] decimal? Price,
        [property: Description("0 to 100000, and not below the variant's reserved stock.")] int? StockOnHand,
        [property: Description("The version from the admin product this edit is based on.")] uint? Version);

    private static Task<IResult> ListProducts(
        [FromQuery(Name = "q"), Description("Case-insensitive substring of the name, the brand or a SKU.")] string? q,
        [FromQuery(Name = "status"), Description("Draft, Active or Archived; every status when omitted.")] string? status,
        [FromQuery(Name = "categorySlug"), Description("Only products of this category.")] string? categorySlug,
        [FromQuery(Name = "page"), Description("1-based page number. Defaults to 1.")] int? page,
        [FromQuery(Name = "pageSize"), Description("Page size, 1 to 100. Defaults to 20.")] int? pageSize,
        IDispatcher dispatcher,
        CancellationToken ct) =>
        Run(dispatcher.Query(new ListAdminProducts(
            q, status, categorySlug, page ?? 1, pageSize ?? ListAdminProducts.DefaultPageSize), ct));

    private static Task<IResult> GetProduct(Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        Run(dispatcher.Query(new GetAdminProduct(id), ct));

    private static Task<IResult> GetImageKeys(IDispatcher dispatcher, CancellationToken ct) =>
        Run(dispatcher.Query(new GetAdminImageKeys(), ct));

    private static Task<IResult> GetCategories(IDispatcher dispatcher, CancellationToken ct) =>
        Run(dispatcher.Query(new GetAdminCategories(), ct));

    private static async Task<IResult> CreateAsync(CreateProductRequest body, IDispatcher dispatcher, CancellationToken ct)
    {
        if (!TryParseSpecs(body.Specs, out var specs, out var specError))
        {
            return specError.ToProblem();
        }

        var result = await dispatcher.Send(new CreateProduct(
            body.CategorySlug, body.Name, body.Brand, body.Description, body.IsFeatured, body.ImageKeys, specs,
            body.OptionAxes, body.Variants), ct);
        return result.IsSuccess
            ? TypedResults.Created($"/api/catalog/admin/products/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateProductRequest body, IDispatcher dispatcher, CancellationToken ct)
    {
        if (!TryParseSpecs(body.Specs, out var specs, out var specError))
        {
            return specError.ToProblem();
        }

        return await Run(dispatcher.Send(new UpdateProduct(
            id, body.Name, body.Brand, body.Description, body.IsFeatured, body.ImageKeys, specs, body.Status, body.Version), ct));
    }

    private static Task<IResult> UpdateVariantAsync(
        Guid id, Guid variantId, UpdateVariantRequest body, IDispatcher dispatcher, CancellationToken ct) =>
        Run(dispatcher.Send(new UpdateVariant(id, variantId, body.Price, body.StockOnHand, body.Version), ct));

    /// <summary>Raw JSON spec values to domain values with the same converter the JSONB column uses. Null stays null.</summary>
    private static bool TryParseSpecs(
        Dictionary<string, JsonElement>? raw, out IReadOnlyDictionary<string, SpecValue>? specs, out Error specError)
    {
        specs = null;
        specError = Error.None;
        if (raw is null)
        {
            return true; // the validator reports the missing field
        }

        var parsed = new Dictionary<string, SpecValue>();
        foreach (var (key, value) in raw)
        {
            if (SpecValuesJsonConverter.FromJson(value) is not { } spec)
            {
                specError = CatalogErrors.InvalidSpecs($"Spec '{key}' must be a number, a string, or true or false.");
                return false;
            }

            parsed[key] = spec;
        }

        specs = parsed;
        return true;
    }

    private static async Task<IResult> Run<T>(Task<Result<T>> pending)
    {
        var result = await pending;
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}
