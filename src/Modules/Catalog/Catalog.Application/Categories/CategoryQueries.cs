using Catalog.Application.Abstractions;
using Catalog.Domain.Categories;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Catalog.Application.Categories;

public sealed record GetCategoryTree : IQuery<IReadOnlyList<CategoryNodeDto>>;

public sealed record GetCategoryFacets(string Slug) : IQuery<CategoryFacetsDto>;

internal sealed class GetCategoryTreeHandler(ICatalogReader reader)
    : IQueryHandler<GetCategoryTree, IReadOnlyList<CategoryNodeDto>>
{
    public async Task<Result<IReadOnlyList<CategoryNodeDto>>> Handle(GetCategoryTree query, CancellationToken ct) =>
        Result.Success(await reader.GetCategoryTreeAsync(ct));
}

internal sealed class GetCategoryFacetsHandler(ICatalogReader reader) : IQueryHandler<GetCategoryFacets, CategoryFacetsDto>
{
    public async Task<Result<CategoryFacetsDto>> Handle(GetCategoryFacets query, CancellationToken ct)
    {
        var facets = string.IsNullOrWhiteSpace(query.Slug) ? null : await reader.GetFacetsAsync(query.Slug, ct);
        return facets is null ? CatalogErrors.CategoryNotFound(query.Slug ?? string.Empty) : facets;
    }
}
