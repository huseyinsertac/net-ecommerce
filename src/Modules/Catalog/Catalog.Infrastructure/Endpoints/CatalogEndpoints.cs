using System.ComponentModel;
using Catalog.Application.Categories;
using Catalog.Application.Products;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shop.Application.Abstractions;
using Shop.Application.Abstractions.Messaging;
using Shop.Application.Abstractions.Paging;
using Shop.Infrastructure.Common.Http;
using Shop.SharedKernel;

namespace Catalog.Infrastructure.Endpoints;

/// <summary>Public, anonymous, read-only catalog API.</summary>
internal static class CatalogEndpoints
{
    private const string Tag = "Catalog";

    private static readonly Dictionary<string, ProductSort> SortValues = Enum.GetValues<ProductSort>()
        .ToDictionary(s => char.ToLowerInvariant(s.ToString()[0]) + s.ToString()[1..], s => s, StringComparer.OrdinalIgnoreCase);

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/categories", GetCategories)
            .WithName("GetCatalogCategories")
            .WithTags(Tag)
            .WithSummary("The category tree with active-product counts.")
            .Produces<IReadOnlyList<CategoryNodeDto>>();

        group.MapGet("/categories/{slug}/facets", GetFacets)
            .WithName("GetCatalogCategoryFacets")
            .WithTags(Tag)
            .WithSummary("Filter facets (brands, price range, spec options) for a category.")
            .Produces<CategoryFacetsDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/products", GetProducts)
            .WithName("SearchCatalogProducts")
            .WithTags(Tag)
            .WithSummary("Searches active products with text, brand, price, stock, spec filters, sorting and paging.")
            .Produces<PagedResult<ProductSummaryDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/products/{slug}", GetProduct)
            .WithName("GetCatalogProduct")
            .WithTags(Tag)
            .WithSummary("Product detail with specs, option axes and variants.")
            .Produces<ProductDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static Task<IResult> GetCategories(IDispatcher dispatcher, CancellationToken ct) =>
        Run(dispatcher.Query(new GetCategoryTree(), ct));

    private static Task<IResult> GetFacets(string slug, IDispatcher dispatcher, CancellationToken ct) =>
        Run(dispatcher.Query(new GetCategoryFacets(slug), ct));

    private static Task<IResult> GetProduct(string slug, IDispatcher dispatcher, CancellationToken ct) =>
        Run(dispatcher.Query(new GetProductBySlug(slug), ct));

    private static async Task<IResult> GetProducts(
        [AsParameters] ProductSearchParameters p, IDispatcher dispatcher, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        var specs = new List<SpecFilter>();
        var specErrors = new List<string>();
        foreach (var raw in p.Spec ?? [])
        {
            if (SpecFilterParser.TryParse(raw, out var filter, out var error))
            {
                specs.Add(filter);
            }
            else
            {
                specErrors.Add($"Invalid spec filter '{raw}': {error}");
            }
        }

        if (specErrors.Count > 0)
        {
            errors["spec"] = [.. specErrors];
        }

        ProductSort? sort = null;
        if (!string.IsNullOrWhiteSpace(p.Sort))
        {
            if (SortValues.TryGetValue(p.Sort.Trim(), out var parsed))
            {
                sort = parsed;
            }
            else
            {
                errors["sort"] = [$"Unknown sort '{p.Sort}'. Expected one of: {string.Join(", ", SortValues.Keys)}."];
            }
        }

        if (errors.Count > 0)
        {
            return new ValidationError(errors).ToProblem();
        }

        var query = new SearchProducts(
            p.Category, p.Q, p.Brand ?? [], p.MinPrice, p.MaxPrice, p.InStock ?? false, p.Featured ?? false, specs, sort,
            p.Page ?? 1, p.PageSize ?? SearchProducts.DefaultPageSize);

        return await Run(dispatcher.Query(query, ct));
    }

    private static async Task<IResult> Run<T>(Task<Result<T>> pending)
    {
        var result = await pending;
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }
}

/// <summary>Query-string parameters of the product search. Repeat <c>brand</c> and <c>spec</c> to pass several.</summary>
internal sealed record ProductSearchParameters(
    [property: Description("Category slug; a parent category includes its descendants.")] [FromQuery(Name = "category")] string? Category,
    [property: Description("Free-text search over name, brand and description; partial trailing words match by prefix.")] [FromQuery(Name = "q")] string? Q,
    [property: Description("Brand name. Repeat the parameter to accept several brands.")] [FromQuery(Name = "brand")] string[]? Brand,
    [property: Description("Minimum price in USD (inclusive).")] [FromQuery(Name = "minPrice")] decimal? MinPrice,
    [property: Description("Maximum price in USD (inclusive).")] [FromQuery(Name = "maxPrice")] decimal? MaxPrice,
    [property: Description("When true, only products with stock available.")] [FromQuery(Name = "inStock")] bool? InStock,
    [property: Description("When true, only featured products.")] [FromQuery(Name = "featured")] bool? Featured,
    [property: Description("Spec filter, repeatable; needs a leaf category. Number range 'ram_gb:16..64' (either bound optional), value list 'os:Windows 11,Linux', boolean 'anc:true'.")] [FromQuery(Name = "spec")] string[]? Spec,
    [property: Description("Sort order: featured, relevance, priceAsc, priceDesc or newest. Defaults to relevance with text, otherwise featured.")] [FromQuery(Name = "sort")] string? Sort,
    [property: Description("1-based page number. Defaults to 1.")] [FromQuery(Name = "page")] int? Page,
    [property: Description("Page size, 1 to 48. Defaults to 24.")] [FromQuery(Name = "pageSize")] int? PageSize);
