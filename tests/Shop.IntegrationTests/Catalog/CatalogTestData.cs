using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Shop.SharedKernel;

namespace Shop.IntegrationTests.Catalog;

/// <summary>
/// Builds catalog entities through the real domain factories. Every dataset carries a random suffix on its slugs and SKUs,
/// so several datasets can live side by side in the shared test database.
/// </summary>
public static class CatalogTestData
{
    public static readonly DateTime CreatedAtUtc = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    public static IReadOnlyList<SpecDefinition> LaptopSchema { get; } =
    [
        new("ram_gb", "RAM", SpecType.Number, "GB", Filterable: true, []),
        new("os", "Operating system", SpecType.Enum, null, Filterable: true, ["Windows 11", "Linux"]),
        new("touchscreen", "Touchscreen", SpecType.Boolean, null, Filterable: true, []),
        new("cpu", "Processor", SpecType.Text, null, Filterable: false, []),
    ];

    public static IReadOnlyList<SpecDefinition> HeadphoneSchema { get; } =
    [
        new("anc", "Active noise cancelling", SpecType.Boolean, null, Filterable: true, []),
        new("driver_mm", "Driver size", SpecType.Number, "mm", Filterable: true, []),
    ];

    public static string NewSuffix() => Guid.NewGuid().ToString("N")[..8];

    public static Category NewCategory(
        string slug, string name, IReadOnlyList<SpecDefinition> schema, Guid? parentId = null, int sortOrder = 0) =>
        Unwrap(Category.Create(slug, name, "devices", parentId, sortOrder, schema));

    public static Product Laptop(
        Category laptops, string suffix, string name = "Arcwing 14 Laptop", decimal ramGb = 16m,
        bool touchscreen = true, int stockOnHand = 5) =>
        Unwrap(Product.Create(
            laptops,
            $"{Slugify(name)}-{suffix}",
            name,
            "Arcwing",
            "A light 14-inch laptop for everyday work.",
            ["arcwing-14-front", "arcwing-14-side"],
            new Dictionary<string, SpecValue>
            {
                ["ram_gb"] = new NumberValue(ramGb),
                ["os"] = new TextValue("Windows 11"),
                ["touchscreen"] = new BoolValue(touchscreen),
                ["cpu"] = new TextValue("Intel Core Ultra 7"),
            },
            [new OptionAxis("color", "Color"), new OptionAxis("storage", "Storage")],
            [
                new NewVariant($"AW14-SLV-512-{suffix.ToUpperInvariant()}",
                    new Dictionary<string, string> { ["color"] = "Silver", ["storage"] = "512 GB" },
                    Money.Usd(1099.99m), stockOnHand),
                new NewVariant($"AW14-BLK-1TB-{suffix.ToUpperInvariant()}",
                    new Dictionary<string, string> { ["color"] = "Black", ["storage"] = "1 TB" },
                    Money.Usd(1299.50m), stockOnHand),
            ],
            isFeatured: true,
            CreatedAtUtc));

    public static Product Headphones(Category headphones, string suffix, int stockOnHand = 1) =>
        Unwrap(Product.Create(
            headphones,
            $"quietline-pro-{suffix}",
            "Quietline Pro Headphones",
            "Quietline",
            "Over-ear wireless headphones with active noise cancelling.",
            ["quietline-pro"],
            new Dictionary<string, SpecValue>
            {
                ["anc"] = new BoolValue(true),
                ["driver_mm"] = new NumberValue(40m),
            },
            [],
            [new NewVariant($"QL-PRO-{suffix.ToUpperInvariant()}", new Dictionary<string, string>(), Money.Usd(249m), stockOnHand)],
            isFeatured: false,
            CreatedAtUtc));

    /// <summary>Creates and saves a small known dataset: one parent category, two leaf categories and one product in each leaf.</summary>
    public static async Task<CatalogDataset> SeedAsync(IServiceProvider services, CancellationToken ct)
    {
        var suffix = NewSuffix();
        var electronics = NewCategory($"electronics-{suffix}", "Electronics", []);
        var laptops = NewCategory($"laptops-{suffix}", "Laptops", LaptopSchema, electronics.Id, sortOrder: 1);
        var headphones = NewCategory($"headphones-{suffix}", "Headphones", HeadphoneSchema, electronics.Id, sortOrder: 2);
        var laptop = Laptop(laptops, suffix);
        var headset = Headphones(headphones, suffix);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Categories.AddRange(electronics, laptops, headphones);
        db.Products.AddRange(laptop, headset);
        await db.SaveChangesAsync(ct);

        return new CatalogDataset(suffix, electronics, laptops, headphones, laptop, headset);
    }

    private static string Slugify(string name) => name.ToLowerInvariant().Replace(' ', '-');

    private static T Unwrap<T>(Result<T> result) =>
        result.IsSuccess ? result.Value : throw new InvalidOperationException($"Test data is invalid: {result.Error.Code}");
}

public sealed record CatalogDataset(
    string Suffix,
    Category Electronics,
    Category Laptops,
    Category Headphones,
    Product Laptop,
    Product Headset);
