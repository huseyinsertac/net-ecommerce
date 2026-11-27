using Catalog.Domain.Categories;
using Catalog.Domain.Products;

namespace Catalog.Application.Abstractions;

/// <summary>Loads and saves products for the admin write side. A product's version is its row's PostgreSQL <c>xmin</c>.</summary>
public interface IProductRepository
{
    /// <summary>The product with all its variants, tracked so changes to it are saved; null when it does not exist.</summary>
    Task<Product?> GetAsync(Guid productId, CancellationToken ct);

    /// <summary>The version the product had when it was loaded.</summary>
    uint VersionOf(Product product);

    Task<Category?> GetCategoryAsync(Guid categoryId, CancellationToken ct);

    Task<Category?> GetCategoryBySlugAsync(string slug, CancellationToken ct);

    Task<bool> HasChildCategoriesAsync(Guid categoryId, CancellationToken ct);

    /// <summary>Existing slugs equal to <paramref name="slug"/> or starting with <paramref name="prefix"/>.</summary>
    Task<IReadOnlyList<string>> GetSlugsAsync(string slug, string prefix, CancellationToken ct);

    /// <summary>Those of <paramref name="skus"/> that some product already uses.</summary>
    Task<IReadOnlyList<string>> GetUsedSkusAsync(IReadOnlyCollection<string> skus, CancellationToken ct);

    /// <summary>Those of <paramref name="imageKeys"/> that some product already uses (so a photo exists for them).</summary>
    Task<IReadOnlyList<string>> GetKnownImageKeysAsync(IReadOnlyCollection<string> imageKeys, CancellationToken ct);

    void Add(Product product);

    /// <summary>Saves a product added with <see cref="Add"/>.</summary>
    Task<ProductSaveOutcome> SaveNewAsync(CancellationToken ct);

    /// <summary>
    /// Saves the changes to a loaded product, only if its version is still <paramref name="expectedVersion"/>. The product
    /// row is always written (even when only variants changed), so every save gives the product a new version.
    /// </summary>
    Task<ProductSaveOutcome> SaveAsync(Product product, uint expectedVersion, CancellationToken ct);
}

public enum ProductSaveOutcome
{
    Saved,

    /// <summary>Someone else saved the product (or one of its variants) first. Nothing was saved.</summary>
    ConcurrentUpdate,

    /// <summary>Another product took the slug first. Nothing was saved.</summary>
    DuplicateSlug,

    /// <summary>Another product took one of the SKUs first. Nothing was saved.</summary>
    DuplicateSku,
}
