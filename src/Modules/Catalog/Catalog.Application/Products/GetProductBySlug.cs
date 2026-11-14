using Catalog.Application.Abstractions;
using Catalog.Domain.Categories;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Catalog.Application.Products;

public sealed record GetProductBySlug(string Slug) : IQuery<ProductDetailDto>;

internal sealed class GetProductBySlugHandler(ICatalogReader reader) : IQueryHandler<GetProductBySlug, ProductDetailDto>
{
    public async Task<Result<ProductDetailDto>> Handle(GetProductBySlug query, CancellationToken ct)
    {
        var product = string.IsNullOrWhiteSpace(query.Slug) ? null : await reader.GetProductAsync(query.Slug, ct);
        return product is null ? CatalogErrors.ProductNotFound(query.Slug ?? string.Empty) : product;
    }
}
