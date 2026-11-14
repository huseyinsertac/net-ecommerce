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
        new("os", "Operating system", SpecType.Enum, null, Filterable: true, ["Windows 11", "Linux", "FreeBSD"]),
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

    public static IReadOnlyList<SpecDefinition> TabletSchema { get; } =
    [
        new("screen_in", "Screen size", SpecType.Number, "in", Filterable: true, []),
    ];

    /// <summary>
    /// Creates and saves the fixed search dataset: 2 parent and 3 leaf categories and 12 products (10 Active, 1 Draft,
    /// 1 Archived). Slugs carry no suffix, so this must only be seeded once, into a database of its own.
    /// </summary>
    public static async Task<SearchDataset> SeedSearchDatasetAsync(IServiceProvider services, CancellationToken ct)
    {
        var electronics = NewCategory("electronics", "Electronics", [], sortOrder: 1);
        var audio = NewCategory("audio", "Audio", [], sortOrder: 2);
        var tablets = NewCategory("tablets", "Tablets", TabletSchema, electronics.Id, sortOrder: 2);
        var laptops = NewCategory("laptops", "Laptops", LaptopSchema, electronics.Id, sortOrder: 1);
        var headphones = NewCategory("headphones", "Headphones", HeadphoneSchema, audio.Id, sortOrder: 1);

        Product LaptopProduct(string slug, string name, string brand, decimal ram, string os, bool touch, bool featured,
            int day, params (string Sku, string Color, string Storage, decimal Price, int Stock)[] variants) =>
            Unwrap(Product.Create(
                laptops, slug, name, brand, $"{name}: a dependable laptop for work and play.",
                [$"{slug}-front", $"{slug}-side"],
                new Dictionary<string, SpecValue>
                {
                    ["ram_gb"] = new NumberValue(ram),
                    ["os"] = new TextValue(os),
                    ["touchscreen"] = new BoolValue(touch),
                    ["cpu"] = new TextValue("Intel Core Ultra 7"),
                },
                [new OptionAxis("color", "Color"), new OptionAxis("storage", "Storage")],
                [.. variants.Select(v => new NewVariant(v.Sku,
                    new Dictionary<string, string> { ["color"] = v.Color, ["storage"] = v.Storage },
                    Money.Usd(v.Price), v.Stock))],
                featured, Day(day)));

        Product TabletProduct(string slug, string name, decimal screen, bool featured, int day,
            params (string Sku, decimal Price, int Stock)[] variants) =>
            Unwrap(Product.Create(
                tablets, slug, name, "Tessel", $"{name}: a slim tablet for reading and drawing.", [slug],
                new Dictionary<string, SpecValue> { ["screen_in"] = new NumberValue(screen) },
                [new OptionAxis("storage", "Storage")],
                [.. variants.Select((v, i) => new NewVariant(v.Sku,
                    new Dictionary<string, string> { ["storage"] = $"{(i + 1) * 128} GB" }, Money.Usd(v.Price), v.Stock))],
                featured, Day(day)));

        Product HeadphoneProduct(string slug, string name, string brand, string description, bool anc, decimal driver,
            bool featured, int day, params (string Sku, decimal Price, int Stock)[] variants) =>
            Unwrap(Product.Create(
                headphones, slug, name, brand, description, [slug],
                new Dictionary<string, SpecValue> { ["anc"] = new BoolValue(anc), ["driver_mm"] = new NumberValue(driver) },
                [new OptionAxis("color", "Color")],
                [.. variants.Select((v, i) => new NewVariant(v.Sku,
                    new Dictionary<string, string> { ["color"] = i == 0 ? "Black" : "White" }, Money.Usd(v.Price), v.Stock))],
                featured, Day(day)));

        var arcwing14 = LaptopProduct("arcwing-14", "Arcwing 14 Laptop", "Arcwing", 16m, "Windows 11", true, true, 1,
            ("AW14-SLV-512", "Silver", "512 GB", 1099.99m, 5), ("AW14-BLK-1TB", "Black", "1 TB", 1299.50m, 5));
        var arcwing16 = LaptopProduct("arcwing-16-pro", "Arcwing 16 Pro Laptop", "Arcwing", 32m, "Linux", false, false, 5,
            ("AW16-GRY-1TB", "Gray", "1 TB", 1899m, 3));
        var nimbus13 = LaptopProduct("nimbus-book-13", "Nimbus Book 13 Laptop", "Nimbus", 8m, "Windows 11", false, false, 3,
            ("NB13-BLU-256", "Blue", "256 GB", 699m, 0));
        var nimbus15 = LaptopProduct("nimbus-book-15", "Nimbus Book 15 Laptop", "Nimbus", 16m, "Linux", true, true, 2,
            ("NB15-BLK-512", "Black", "512 GB", 899m, 2));
        var corvidAir = LaptopProduct("corvid-air", "Corvid Air Laptop", "Corvid", 8m, "Linux", false, false, 10,
            ("CA-WHT-256", "White", "256 GB", 549m, 7));
        var draftLaptop = LaptopProduct("arcwing-x-draft", "Arcwing X Laptop", "Arcwing", 64m, "Windows 11", true, true, 11,
            ("AWX-BLK-2TB", "Black", "2 TB", 2999m, 9));

        var slate10 = TabletProduct("tessel-slate-10", "Tessel Slate 10", 10.90m, false, 4, ("TS10-128", 399m, 10));
        var slate12 = TabletProduct("tessel-slate-12", "Tessel Slate 12", 12m, true, 6,
            ("TS12-128", 549m, 4), ("TS12-256", 649m, 4));
        var archivedTablet = TabletProduct("tessel-slate-old", "Tessel Slate Classic", 9.70m, false, 12, ("TSO-128", 199m, 8));

        var quietlinePro = HeadphoneProduct("quietline-pro", "Quietline Pro Headphones", "Quietline",
            "Over-ear wireless headphones with active noise cancelling.", true, 40m, false, 7, ("QL-PRO-BLK", 249m, 1));
        var quietlineLite = HeadphoneProduct("quietline-lite", "Quietline Lite Headphones", "Quietline",
            "Lightweight on-ear headphones for the commute.", false, 32m, false, 8, ("QL-LITE-BLK", 99m, 20));
        var pulseBuds = HeadphoneProduct("pulse-buds", "Pulse Buds", "PulseWave",
            "Tiny earbuds with adaptive noise cancelling.", true, 12m, true, 9,
            ("PB-BLK", 129m, 0), ("PB-WHT", 149m, 5));

        Product[] active =
            [arcwing14, arcwing16, nimbus13, nimbus15, corvidAir, slate10, slate12, quietlinePro, quietlineLite, pulseBuds];
        foreach (var product in active.Append(archivedTablet))
        {
            Ensure(product.Publish());
        }

        Ensure(archivedTablet.Archive());
        Ensure(nimbus15.ReserveStock(nimbus15.Variants.Single().Id, 2)); // fully reserved

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Categories.AddRange(electronics, audio, laptops, tablets, headphones);
        db.Products.AddRange([.. active, draftLaptop, archivedTablet]);
        await db.SaveChangesAsync(ct);

        return new SearchDataset(electronics, audio, laptops, tablets, headphones, active, draftLaptop, archivedTablet);
    }

    private static DateTime Day(int day) => new(2026, 11, day, 9, 0, 0, DateTimeKind.Utc);

    private static void Ensure(Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Test data is invalid: {result.Error.Code}");
        }
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

public sealed record SearchDataset(
    Category Electronics,
    Category Audio,
    Category Laptops,
    Category Tablets,
    Category Headphones,
    IReadOnlyList<Product> Active,
    Product Draft,
    Product Archived);
