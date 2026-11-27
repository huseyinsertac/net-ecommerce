using Catalog.Application.Abstractions;
using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Catalog.Infrastructure.Persistence;

internal sealed class ProductRepository(CatalogDbContext db, TimeProvider clock) : IProductRepository
{
    public Task<Product?> GetAsync(Guid productId, CancellationToken ct) =>
        db.Products.Include(p => p.Variants).SingleOrDefaultAsync(p => p.Id == productId, ct);

    public uint VersionOf(Product product) =>
        db.Entry(product).Property<uint>(ProductConfiguration.RowVersion).CurrentValue;

    public Task<Category?> GetCategoryAsync(Guid categoryId, CancellationToken ct) =>
        db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == categoryId, ct);

    public Task<Category?> GetCategoryBySlugAsync(string slug, CancellationToken ct) =>
        db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Slug == slug, ct);

    public Task<bool> HasChildCategoriesAsync(Guid categoryId, CancellationToken ct) =>
        db.Categories.AnyAsync(c => c.ParentId == categoryId, ct);

    public async Task<IReadOnlyList<string>> GetSlugsAsync(string slug, string prefix, CancellationToken ct) =>
        await db.Products.Where(p => p.Slug == slug || p.Slug.StartsWith(prefix)).Select(p => p.Slug).ToListAsync(ct);

    public async Task<IReadOnlyList<string>> GetUsedSkusAsync(IReadOnlyCollection<string> skus, CancellationToken ct)
    {
        var wanted = skus.ToArray();
        return await db.Products.SelectMany(p => p.Variants).Where(v => wanted.Contains(v.Sku)).Select(v => v.Sku).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetKnownImageKeysAsync(IReadOnlyCollection<string> imageKeys, CancellationToken ct)
    {
        var wanted = imageKeys.ToArray();
        return await db.Database
            .SqlQuery<string>($"SELECT DISTINCT k AS \"Value\" FROM catalog.products p CROSS JOIN LATERAL unnest(p.image_keys) AS k WHERE k = ANY({wanted})")
            .ToListAsync(ct);
    }

    public void Add(Product product) => db.Products.Add(product);

    public Task<ProductSaveOutcome> SaveNewAsync(CancellationToken ct) => SaveChangesAsync(ct);

    public Task<ProductSaveOutcome> SaveAsync(Product product, uint expectedVersion, CancellationToken ct)
    {
        var entry = db.Entry(product);
        // The UPDATE's WHERE xmin = ... uses the version the caller read, not just the one loaded in this request, so a
        // save racing another one between the load and this write is refused too.
        entry.Property<uint>(ProductConfiguration.RowVersion).OriginalValue = expectedVersion;
        // Always write the product row, so its xmin (the version) changes even when only a variant did.
        var updatedAt = entry.Property<DateTime?>(ProductConfiguration.UpdatedAtUtc);
        updatedAt.CurrentValue = clock.GetUtcNow().UtcDateTime;
        // Even a frozen clock (same value as stored) must still produce an UPDATE, or xmin would not move.
        updatedAt.IsModified = true;
        return SaveChangesAsync(ct);
    }

    private async Task<ProductSaveOutcome> SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return ProductSaveOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return ProductSaveOutcome.ConcurrentUpdate;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           {
                                               SqlState: PostgresErrorCodes.UniqueViolation,
                                               ConstraintName: ProductConfiguration.SlugIndex or ProductVariantConfiguration.SkuIndex,
                                           } unique)
        {
            db.ChangeTracker.Clear();
            return unique.ConstraintName == ProductConfiguration.SlugIndex
                ? ProductSaveOutcome.DuplicateSlug
                : ProductSaveOutcome.DuplicateSku;
        }
    }
}
