using System.Text.RegularExpressions;
using Catalog.Domain.Categories;
using Shop.SharedKernel;

namespace Catalog.Domain.Products;

public sealed partial class Product : AggregateRoot<Guid>
{
    private const int MaxNameLength = 200;
    private const int MaxSlugLength = 120;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("^[A-Z0-9-]+$")]
    private static partial Regex SkuPattern();

    private List<string> _imageKeys = [];
    private Dictionary<string, SpecValue> _specs = [];
    private List<OptionAxis> _optionAxes = [];
    private List<ProductVariant> _variants = [];

    private Product() { }

    public string Slug { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public Guid CategoryId { get; private set; }
    public IReadOnlyList<string> ImageKeys => _imageKeys;
    public IReadOnlyDictionary<string, SpecValue> Specs => _specs;
    public IReadOnlyList<OptionAxis> OptionAxes => _optionAxes;
    public IReadOnlyList<ProductVariant> Variants => _variants;
    public ProductStatus Status { get; private set; }
    public bool IsFeatured { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Result<Product> Create(
        Category category, string slug, string name, string brand, string description,
        IReadOnlyList<string> imageKeys, IReadOnlyDictionary<string, SpecValue> specs,
        IReadOnlyList<OptionAxis> optionAxes, IReadOnlyList<NewVariant> variants,
        bool isFeatured, DateTime createdAtUtc)
    {
        if (category is null)
        {
            return CatalogErrors.InvalidProduct("Category must be provided.");
        }

        if (string.IsNullOrEmpty(slug) || slug.Length > MaxSlugLength || !SlugPattern().IsMatch(slug))
        {
            return CatalogErrors.InvalidSlug(slug ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
        {
            return CatalogErrors.InvalidProduct($"Name must be 1-{MaxNameLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(brand))
        {
            return CatalogErrors.InvalidName("Brand");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return CatalogErrors.InvalidName("Description");
        }

        if (imageKeys is null || imageKeys.Count == 0 || imageKeys.Any(string.IsNullOrWhiteSpace))
        {
            return CatalogErrors.InvalidProduct("At least one non-blank image key is required.");
        }

        if (specs is null)
        {
            return CatalogErrors.NullSpecValues();
        }

        var specsCopy = new Dictionary<string, SpecValue>(specs);
        var specResult = category.ValidateSpecs(specsCopy);
        if (specResult.IsFailure)
        {
            return specResult.Error;
        }

        if (optionAxes is null
            || optionAxes.Any(a => a is null || string.IsNullOrWhiteSpace(a.Key) || string.IsNullOrWhiteSpace(a.Label)))
        {
            return CatalogErrors.InvalidVariantOptions("Option axes need a key and a label.");
        }

        var axes = optionAxes.ToList();
        if (axes.Select(a => a.Key).Distinct().Count() != axes.Count)
        {
            return CatalogErrors.InvalidVariantOptions("Option axis keys must be unique.");
        }

        if (variants is null || variants.Count == 0 || variants.Any(v => v is null))
        {
            return CatalogErrors.InvalidProduct("At least one variant is required.");
        }

        var built = new List<ProductVariant>();
        var skus = new HashSet<string>();
        var combinations = new HashSet<string>();
        foreach (var v in variants)
        {
            if (string.IsNullOrEmpty(v.Sku) || !SkuPattern().IsMatch(v.Sku))
            {
                return CatalogErrors.InvalidSku(v.Sku ?? string.Empty);
            }

            if (!skus.Add(v.Sku))
            {
                return CatalogErrors.DuplicateSku(v.Sku);
            }

            if (v.Price is null)
            {
                return CatalogErrors.InvalidPrice($"SKU '{v.Sku}' needs a price.");
            }

            if (v.Price.Currency != Money.UsdCurrency)
            {
                return CatalogErrors.InvalidPrice($"SKU '{v.Sku}' must be priced in USD.");
            }

            if (v.Price.Amount <= 0)
            {
                return CatalogErrors.InvalidPrice($"SKU '{v.Sku}' must have a price greater than zero.");
            }

            if (v.StockOnHand < 0)
            {
                return CatalogErrors.InvalidStock(v.Sku);
            }

            if (v.Options is null
                || v.Options.Count != axes.Count
                || axes.Any(a => !v.Options.TryGetValue(a.Key, out var value) || string.IsNullOrWhiteSpace(value)))
            {
                return CatalogErrors.InvalidVariantOptions(
                    $"SKU '{v.Sku}' must have a non-blank value for exactly the option axes.");
            }

            var combination = string.Join('\u001f', axes.Select(a => v.Options[a.Key]));
            if (!combinations.Add(combination))
            {
                return CatalogErrors.InvalidVariantOptions($"SKU '{v.Sku}' repeats an existing option combination.");
            }

            built.Add(ProductVariant.Create(v.Sku, new Dictionary<string, string>(v.Options), v.Price, v.StockOnHand));
        }

        return new Product
        {
            Id = Guid.CreateVersion7(),
            Slug = slug,
            Name = name,
            Brand = brand,
            Description = description,
            CategoryId = category.Id,
            _imageKeys = [.. imageKeys],
            _specs = specsCopy,
            _optionAxes = axes,
            _variants = built,
            Status = ProductStatus.Draft,
            IsFeatured = isFeatured,
            CreatedAtUtc = createdAtUtc,
        };
    }

    public Result Publish()
    {
        if (Status == ProductStatus.Active)
        {
            return Result.Failure(CatalogErrors.InvalidStatusTransition(Status.ToString(), nameof(ProductStatus.Active)));
        }

        Status = ProductStatus.Active;
        return Result.Success();
    }

    public Result Archive()
    {
        if (Status != ProductStatus.Active)
        {
            return Result.Failure(CatalogErrors.InvalidStatusTransition(Status.ToString(), nameof(ProductStatus.Archived)));
        }

        Status = ProductStatus.Archived;
        return Result.Success();
    }

    public Result ReserveStock(Guid variantId, int quantity)
    {
        var check = Locate(variantId, quantity, out var variant);
        if (check.IsFailure)
        {
            return check;
        }

        if (quantity > variant!.Available)
        {
            return Result.Failure(CatalogErrors.InsufficientStock(variant.Sku));
        }

        variant.Reserve(quantity);
        return Result.Success();
    }

    public Result ReleaseStock(Guid variantId, int quantity)
    {
        var check = Locate(variantId, quantity, out var variant);
        if (check.IsFailure)
        {
            return check;
        }

        if (quantity > variant!.StockReserved)
        {
            return Result.Failure(CatalogErrors.ReleaseExceedsReserved(variant.Sku));
        }

        variant.Release(quantity);
        return Result.Success();
    }

    public Result CommitStock(Guid variantId, int quantity)
    {
        var check = Locate(variantId, quantity, out var variant);
        if (check.IsFailure)
        {
            return check;
        }

        if (quantity > variant!.StockReserved)
        {
            return Result.Failure(CatalogErrors.ReleaseExceedsReserved(variant.Sku));
        }

        variant.Commit(quantity);
        return Result.Success();
    }

    private Result Locate(Guid variantId, int quantity, out ProductVariant? variant)
    {
        variant = null;
        if (quantity <= 0)
        {
            return Result.Failure(CatalogErrors.InvalidQuantity());
        }

        variant = _variants.Find(v => v.Id == variantId);
        return variant is null
            ? Result.Failure(CatalogErrors.VariantNotFound(variantId))
            : Result.Success();
    }
}
