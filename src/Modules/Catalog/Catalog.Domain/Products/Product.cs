using System.Text.RegularExpressions;
using Catalog.Domain.Categories;
using Shop.SharedKernel;

namespace Catalog.Domain.Products;

public sealed partial class Product : AggregateRoot<Guid>
{
    public const int MaxNameLength = 200;
    public const int MaxSlugLength = 120;
    public const int MaxStock = 100_000;
    public const decimal MinPrice = 0.01m;
    public const decimal MaxPrice = 100_000m;

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

        if (CheckDetails(name, brand, description, imageKeys) is { } detailsError)
        {
            return detailsError;
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

            if (CheckPrice(v.Sku, v.Price) is { } priceError)
            {
                return priceError;
            }

            if (v.StockOnHand is < 0 or > MaxStock)
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

    /// <summary>Replaces the editable details. The slug never changes, so existing links keep working.</summary>
    public Result UpdateDetails(
        string name, string brand, string description, bool isFeatured, IReadOnlyList<string> imageKeys)
    {
        if (CheckDetails(name, brand, description, imageKeys) is { } error)
        {
            return Result.Failure(error);
        }

        Name = name;
        Brand = brand;
        Description = description;
        IsFeatured = isFeatured;
        _imageKeys = [.. imageKeys];
        return Result.Success();
    }

    /// <summary>Replaces all spec values; <paramref name="category"/> must be this product's category.</summary>
    public Result SetSpecs(Category category, IReadOnlyDictionary<string, SpecValue> specs)
    {
        if (category is null || category.Id != CategoryId)
        {
            return Result.Failure(CatalogErrors.InvalidSpecs("Specs must be validated against the product's own category."));
        }

        if (specs is null)
        {
            return Result.Failure(CatalogErrors.NullSpecValues());
        }

        var copy = new Dictionary<string, SpecValue>(specs);
        var valid = category.ValidateSpecs(copy);
        if (valid.IsFailure)
        {
            return valid;
        }

        _specs = copy;
        return Result.Success();
    }

    /// <summary>The admin's status switch: Draft, Active or Archived, in any direction (the same status is a no-op).</summary>
    public Result SetStatus(ProductStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            return Result.Failure(CatalogErrors.InvalidProduct($"Unknown status '{status}'."));
        }

        if (status == ProductStatus.Active && _variants.Count == 0)
        {
            return Result.Failure(CatalogErrors.InvalidProduct("An Active product needs at least one variant."));
        }

        Status = status;
        return Result.Success();
    }

    public Result SetVariantPrice(Guid variantId, Money price)
    {
        var variant = _variants.Find(v => v.Id == variantId);
        if (variant is null)
        {
            return Result.Failure(CatalogErrors.VariantNotFound(variantId));
        }

        if (CheckPrice(variant.Sku, price) is { } error)
        {
            return Result.Failure(error);
        }

        variant.SetPrice(price);
        return Result.Success();
    }

    /// <summary>Sets the units on hand, never below what open orders have reserved.</summary>
    public Result SetVariantStock(Guid variantId, int stockOnHand)
    {
        var variant = _variants.Find(v => v.Id == variantId);
        if (variant is null)
        {
            return Result.Failure(CatalogErrors.VariantNotFound(variantId));
        }

        if (stockOnHand is < 0 or > MaxStock)
        {
            return Result.Failure(CatalogErrors.InvalidStock(variant.Sku));
        }

        if (stockOnHand < variant.StockReserved)
        {
            return Result.Failure(CatalogErrors.StockBelowReserved(variant.Sku, variant.StockReserved));
        }

        variant.SetStockOnHand(stockOnHand);
        return Result.Success();
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

    private static Error? CheckDetails(string name, string brand, string description, IReadOnlyList<string> imageKeys)
    {
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

        return null;
    }

    private static Error? CheckPrice(string sku, Money? price)
    {
        if (price is null)
        {
            return CatalogErrors.InvalidPrice($"SKU '{sku}' needs a price.");
        }

        if (price.Currency != Money.UsdCurrency)
        {
            return CatalogErrors.InvalidPrice($"SKU '{sku}' must be priced in USD.");
        }

        return price.Amount is < MinPrice or > MaxPrice
            ? CatalogErrors.InvalidPrice($"SKU '{sku}' must cost between 0.01 and 100000.00 USD.")
            : null;
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
