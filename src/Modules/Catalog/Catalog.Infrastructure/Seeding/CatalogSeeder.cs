using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shop.Infrastructure.Common.Modules;
using Shop.SharedKernel;

namespace Catalog.Infrastructure.Seeding;

/// <summary>
/// Seeds the demo catalog from the embedded <c>catalog-seed.json</c>. Does nothing when any category already exists.
/// Everything goes through the domain factories, so invalid seed data fails loudly with the domain error.
/// </summary>
internal sealed class CatalogSeeder(CatalogDbContext db, ILogger<CatalogSeeder> logger) : IModuleSeeder
{
    private const string ResourceName = "Catalog.Infrastructure.Seeding.catalog-seed.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public string Module => "catalog";

    public async Task SeedAsync(CancellationToken ct)
    {
        if (await db.Categories.AnyAsync(ct))
        {
            logger.LogInformation("Catalog already has categories; skipping seed");
            return;
        }

        var seed = Load();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var categories = BuildCategories(seed.Categories);
        db.Categories.AddRange(categories.Values);

        var products = seed.Products.Select(p => BuildProduct(p, categories)).ToList();
        db.Products.AddRange(products);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Seeded catalog with {Categories} categories and {Products} products", categories.Count, products.Count);
    }

    private static SeedFile Load()
    {
        using var stream = typeof(CatalogSeeder).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' was not found.");
        return JsonSerializer.Deserialize<SeedFile>(stream, JsonOptions)
            ?? throw new InvalidOperationException("The catalog seed file is empty.");
    }

    private static Dictionary<string, Category> BuildCategories(IReadOnlyList<SeedCategory> seeds)
    {
        var bySlug = new Dictionary<string, Category>();
        foreach (var seed in seeds.OrderBy(c => c.Parent is null ? 0 : 1))
        {
            Guid? parentId = null;
            if (seed.Parent is not null)
            {
                parentId = bySlug.TryGetValue(seed.Parent, out var parent)
                    ? parent.Id
                    : throw new InvalidOperationException($"Category '{seed.Slug}' names unknown parent '{seed.Parent}'.");
            }

            var schema = seed.Specs
                .Select(s => new SpecDefinition(s.Key, s.Label, Enum.Parse<SpecType>(s.Type), s.Unit, s.Filterable, s.Options))
                .ToList();
            bySlug.Add(seed.Slug, Unwrap(Category.Create(seed.Slug, seed.Name, seed.Icon, parentId, seed.SortOrder, schema)));
        }

        return bySlug;
    }

    private static Product BuildProduct(SeedProduct seed, IReadOnlyDictionary<string, Category> categories)
    {
        var category = categories.GetValueOrDefault(seed.Category)
            ?? throw new InvalidOperationException($"Product '{seed.Slug}' names unknown category '{seed.Category}'.");

        var specs = seed.Specs.ToDictionary(kv => kv.Key, kv => ToSpecValue(seed.Slug, kv.Key, kv.Value));
        var axes = seed.OptionAxes.Select(a => new OptionAxis(a.Key, a.Label)).ToList();
        var variants = seed.Variants
            .Select(v => new NewVariant(v.Sku, v.Options, Money.Usd(v.Price), v.Stock))
            .ToList();

        var product = Unwrap(Product.Create(
            category, seed.Slug, seed.Name, seed.Brand, seed.Description, seed.Images, specs, axes, variants,
            seed.Featured, DateTime.SpecifyKind(seed.CreatedAt, DateTimeKind.Utc)));

        if (seed.Published)
        {
            Unwrap(product.Publish());
        }

        return product;
    }

    private static SpecValue ToSpecValue(string product, string key, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => new NumberValue(value.GetDecimal()),
        JsonValueKind.String => new TextValue(value.GetString()!),
        JsonValueKind.True => new BoolValue(true),
        JsonValueKind.False => new BoolValue(false),
        _ => throw new InvalidOperationException($"Product '{product}' has an unsupported value for spec '{key}'."),
    };

    private static T Unwrap<T>(Result<T> result) =>
        result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException($"Catalog seed is invalid: {result.Error.Code}: {result.Error.Message}");

    private static void Unwrap(Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Catalog seed is invalid: {result.Error.Code}: {result.Error.Message}");
        }
    }
}
