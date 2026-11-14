using Catalog.Application.Abstractions;
using Catalog.Domain.Categories;
using FluentValidation;
using Shop.Application.Abstractions;
using Shop.Application.Abstractions.Messaging;
using Shop.Application.Abstractions.Paging;
using Shop.SharedKernel;

namespace Catalog.Application.Products;

public enum ProductSort { Featured, Relevance, PriceAsc, PriceDesc, Newest }

/// <summary>A spec filter: a range (Number specs) or a list of accepted values (Enum, Text and Boolean specs).</summary>
public sealed record SpecFilter(string Key, decimal? Min, decimal? Max, IReadOnlyList<string> Values);

/// <param name="Sort">When null: <see cref="ProductSort.Relevance"/> if there is text, otherwise <see cref="ProductSort.Featured"/>.</param>
public sealed record SearchProducts(
    string? Category,
    string? Text,
    IReadOnlyList<string> Brands,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool InStockOnly,
    bool FeaturedOnly,
    IReadOnlyList<SpecFilter> Specs,
    ProductSort? Sort = null,
    int Page = 1,
    int PageSize = SearchProducts.DefaultPageSize) : IQuery<PagedResult<ProductSummaryDto>>
{
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 48;
}

internal sealed class SearchProductsValidator : AbstractValidator<SearchProducts>
{
    public SearchProductsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, SearchProducts.MaxPageSize);
        RuleFor(x => x.Text).MaximumLength(200);
        RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0m).When(x => x.MinPrice.HasValue);
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(0m).When(x => x.MaxPrice.HasValue);
        RuleFor(x => x.MaxPrice)
            .Must((x, max) => x.MinPrice <= max)
            .When(x => x.MinPrice.HasValue && x.MaxPrice.HasValue)
            .WithMessage("'Max Price' must be greater than or equal to 'Min Price'.");
        RuleFor(x => x.Brands).Must(b => b is null || b.Count <= 20).WithMessage("At most 20 brands are allowed.");
        RuleFor(x => x.Specs).Must(s => s is null || s.Count <= 20).WithMessage("At most 20 spec filters are allowed.");
    }
}

internal sealed class SearchProductsHandler(ICatalogReader reader)
    : IQueryHandler<SearchProducts, PagedResult<ProductSummaryDto>>
{
    private const string SpecField = "spec";

    public async Task<Result<PagedResult<ProductSummaryDto>>> Handle(SearchProducts query, CancellationToken ct)
    {
        var specs = query.Specs ?? [];

        CategoryInfo? category = null;
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            category = await reader.GetCategoryAsync(query.Category, ct);
            if (category is null)
            {
                return CatalogErrors.CategoryNotFound(query.Category);
            }
        }

        var resolved = new List<(SpecDefinition Def, SpecFilter Filter)>();
        if (specs.Count > 0)
        {
            var errors = ResolveSpecs(category, specs, resolved);
            if (errors.Count > 0)
            {
                return new ValidationError(new Dictionary<string, string[]> { [SpecField] = [.. errors.Distinct()] });
            }
        }

        var brands = (query.Brands ?? [])
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => b.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sort = query.Sort ?? (string.IsNullOrWhiteSpace(query.Text) ? ProductSort.Featured : ProductSort.Relevance);

        var criteria = new ProductSearchCriteria(
            category?.CategoryIds, query.Text, brands, query.MinPrice, query.MaxPrice, query.InStockOnly,
            query.FeaturedOnly, resolved, sort, query.Page, query.PageSize);

        return await reader.SearchAsync(criteria, ct);
    }

    private static List<string> ResolveSpecs(
        CategoryInfo? category, IReadOnlyList<SpecFilter> specs, List<(SpecDefinition, SpecFilter)> resolved)
    {
        if (category is null)
        {
            return ["Spec filters require a category."];
        }

        if (!category.IsLeaf)
        {
            return ["Spec filters require a leaf category."];
        }

        var errors = new List<string>();
        foreach (var filter in specs)
        {
            var error = Check(category, filter, out var definition);
            if (error is null)
            {
                resolved.Add((definition!, filter with { Values = filter.Values ?? [] }));
            }
            else
            {
                errors.Add(error);
            }
        }

        return errors;
    }

    private static string? Check(CategoryInfo category, SpecFilter? filter, out SpecDefinition? definition)
    {
        definition = null;
        if (filter is null)
        {
            return "Spec filters must not be null.";
        }

        var key = filter.Key ?? string.Empty;
        definition = category.SpecSchema.FirstOrDefault(s => s.Filterable && s.Key == key);
        if (definition is null)
        {
            return $"Unknown or non-filterable spec '{key}'.";
        }

        var values = filter.Values ?? [];
        var hasRange = filter.Min.HasValue || filter.Max.HasValue;

        if (definition.Type == SpecType.Number)
        {
            if (values.Count > 0)
            {
                return $"Spec '{key}' does not support values.";
            }

            if (!hasRange)
            {
                return $"Spec '{key}' needs a minimum or a maximum.";
            }

            return filter.Min > filter.Max ? $"Spec '{key}' has a minimum greater than its maximum." : null;
        }

        if (hasRange)
        {
            return $"Spec '{key}' does not support ranges.";
        }

        if (values.Count == 0 || values.Any(string.IsNullOrEmpty))
        {
            return $"Spec '{key}' needs at least one non-empty value.";
        }

        if (definition.Type == SpecType.Boolean && values.Any(v => v is not ("true" or "false")))
        {
            return $"Spec '{key}' values must be true or false.";
        }

        return null;
    }
}
