using Catalog.Application.Abstractions;
using Catalog.Domain.Categories;
using FluentValidation;
using Shop.Application.Abstractions.Messaging;
using Shop.Application.Abstractions.Paging;
using Shop.SharedKernel;

namespace Catalog.Application.Admin;

/// <param name="Status">"Draft", "Active" or "Archived" (any case); null for every status.</param>
public sealed record ListAdminProducts(string? Text, string? Status, string? CategorySlug, int Page = 1, int PageSize = ListAdminProducts.DefaultPageSize)
    : IQuery<PagedResult<AdminProductSummaryDto>>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}

internal sealed class ListAdminProductsValidator : AbstractValidator<ListAdminProducts>
{
    public ListAdminProductsValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 100_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, ListAdminProducts.MaxPageSize);
        RuleFor(x => x.Text).MaximumLength(200);
        RuleFor(x => x.CategorySlug).MaximumLength(80);
        RuleFor(x => x.Status)
            .Must(s => string.IsNullOrEmpty(s) || AdminMapping.ParseStatus(s) is not null)
            .WithMessage("Status must be Draft, Active or Archived.");
    }
}

internal sealed class ListAdminProductsHandler(IAdminCatalogReader reader)
    : IQueryHandler<ListAdminProducts, PagedResult<AdminProductSummaryDto>>
{
    public async Task<Result<PagedResult<AdminProductSummaryDto>>> Handle(ListAdminProducts query, CancellationToken ct) =>
        await reader.ListProductsAsync(new AdminProductListCriteria(
            string.IsNullOrWhiteSpace(query.Text) ? null : query.Text.Trim(),
            AdminMapping.ParseStatus(query.Status),
            string.IsNullOrWhiteSpace(query.CategorySlug) ? null : query.CategorySlug.Trim(),
            query.Page,
            query.PageSize), ct);
}

public sealed record GetAdminProduct(Guid ProductId) : IQuery<AdminProductDto>;

internal sealed class GetAdminProductHandler(IAdminCatalogReader reader) : IQueryHandler<GetAdminProduct, AdminProductDto>
{
    public async Task<Result<AdminProductDto>> Handle(GetAdminProduct query, CancellationToken ct) =>
        await reader.GetProductAsync(query.ProductId, ct) is { } product
            ? product
            : CatalogErrors.ProductNotFound(query.ProductId.ToString());
}

public sealed record GetAdminCategories : IQuery<IReadOnlyList<AdminCategoryDto>>;

internal sealed class GetAdminCategoriesHandler(IAdminCatalogReader reader)
    : IQueryHandler<GetAdminCategories, IReadOnlyList<AdminCategoryDto>>
{
    public async Task<Result<IReadOnlyList<AdminCategoryDto>>> Handle(GetAdminCategories query, CancellationToken ct) =>
        Result.Success(await reader.GetLeafCategoriesAsync(ct));
}

public sealed record GetAdminImageKeys : IQuery<IReadOnlyList<AdminImageKeyDto>>;

internal sealed class GetAdminImageKeysHandler(IAdminCatalogReader reader)
    : IQueryHandler<GetAdminImageKeys, IReadOnlyList<AdminImageKeyDto>>
{
    public async Task<Result<IReadOnlyList<AdminImageKeyDto>>> Handle(GetAdminImageKeys query, CancellationToken ct) =>
        Result.Success(await reader.GetImageKeysAsync(ct));
}
