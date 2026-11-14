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
}
