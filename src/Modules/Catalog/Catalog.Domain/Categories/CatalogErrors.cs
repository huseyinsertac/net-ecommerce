using Shop.SharedKernel;

namespace Catalog.Domain.Categories;

public static class CatalogErrors
{
    public static Error InvalidSlug(string slug) =>
        Error.Validation("catalog.invalid_slug",
            $"Slug '{slug}' must be lowercase alphanumeric words separated by hyphens, at most 80 characters.");

    public static Error InvalidName(string field) =>
        Error.Validation("catalog.invalid_name", $"{field} must not be blank.");

    public static Error InvalidSpecSchema(string message) =>
        Error.Validation("catalog.invalid_spec_schema", message);

    public static Error NullSpecValues() =>
        Error.Validation("catalog.spec_unknown", "Spec values must be provided.");

    public static Error SpecMissing(string key) =>
        Error.Validation("catalog.spec_missing", $"Spec '{key}' is required.");

    public static Error SpecUnknown(string key) =>
        Error.Validation("catalog.spec_unknown", $"Spec '{key}' is not defined for this category.");

    public static Error SpecTypeMismatch(string key) =>
        Error.Validation("catalog.spec_type_mismatch", $"Spec '{key}' has a value of the wrong type.");

    public static Error SpecInvalidOption(string key) =>
        Error.Validation("catalog.spec_invalid_option", $"Spec '{key}' has a value that is not one of its allowed options.");

    public static Error InvalidProduct(string message) =>
        Error.Validation("catalog.invalid_product", message);

    public static Error InvalidSku(string sku) =>
        Error.Validation("catalog.invalid_sku", $"SKU '{sku}' must be non-empty and match ^[A-Z0-9-]+$.");

    public static Error DuplicateSku(string sku) =>
        Error.Validation("catalog.duplicate_sku", $"SKU '{sku}' is used more than once in this product.");

    public static Error InvalidPrice(string message) =>
        Error.Validation("catalog.invalid_price", message);

    public static Error InvalidStock(string sku) =>
        Error.Validation("catalog.invalid_stock", $"Stock for SKU '{sku}' must not be negative.");

    public static Error InvalidVariantOptions(string message) =>
        Error.Validation("catalog.invalid_variant_options", message);

    public static Error InvalidStatusTransition(string from, string to) =>
        Error.Conflict("catalog.invalid_status_transition", $"A {from} product cannot become {to}.");

    public static Error InvalidQuantity() =>
        Error.Validation("catalog.invalid_quantity", "Quantity must be greater than zero.");

    public static Error VariantNotFound(Guid variantId) =>
        Error.NotFound("catalog.variant_not_found", $"Variant '{variantId}' was not found on this product.");

    public static Error InsufficientStock(string sku) =>
        Error.Conflict("catalog.insufficient_stock", $"Not enough available stock for SKU '{sku}'.");

    public static Error ReleaseExceedsReserved(string sku) =>
        Error.Conflict("catalog.release_exceeds_reserved", $"Quantity exceeds the reserved stock for SKU '{sku}'.");

    public static Error CategoryNotFound(string slug) =>
        Error.NotFound("catalog.category_not_found", $"Category '{slug}' was not found.");

    public static Error ProductNotFound(string slug) =>
        Error.NotFound("catalog.product_not_found", $"Product '{slug}' was not found.");
}
