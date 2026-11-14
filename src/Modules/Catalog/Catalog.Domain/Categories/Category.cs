using System.Text.RegularExpressions;
using Shop.SharedKernel;

namespace Catalog.Domain.Categories;

public sealed partial class Category : AggregateRoot<Guid>
{
    private const int MaxSlugLength = 80;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex SpecKeyPattern();

    private List<SpecDefinition> _specSchema = [];

    private Category() { }

    public string Slug { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Icon { get; private set; } = string.Empty;
    public Guid? ParentId { get; private set; }
    public int SortOrder { get; private set; }
    public IReadOnlyList<SpecDefinition> SpecSchema => _specSchema;

    public static Result<Category> Create(
        string slug, string name, string icon, Guid? parentId, int sortOrder,
        IReadOnlyList<SpecDefinition> specSchema)
    {
        if (string.IsNullOrEmpty(slug) || slug.Length > MaxSlugLength || !SlugPattern().IsMatch(slug))
        {
            return CatalogErrors.InvalidSlug(slug ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return CatalogErrors.InvalidName("Name");
        }

        if (string.IsNullOrWhiteSpace(icon))
        {
            return CatalogErrors.InvalidName("Icon");
        }

        if (specSchema is null)
        {
            return CatalogErrors.InvalidSpecSchema("Spec schema must not be null.");
        }

        var schemaError = ValidateSchema(specSchema);
        if (schemaError is not null)
        {
            return schemaError;
        }

        var copied = specSchema.Select(spec => spec with { Options = [.. spec.Options] }).ToList();

        return new Category
        {
            Id = Guid.CreateVersion7(),
            Slug = slug,
            Name = name,
            Icon = icon,
            ParentId = parentId,
            SortOrder = sortOrder,
            _specSchema = copied,
        };
    }

    public SpecDefinition? FindSpec(string key) => _specSchema.Find(s => s.Key == key);

    public Result ValidateSpecs(IReadOnlyDictionary<string, SpecValue> values)
    {
        if (values is null)
        {
            return Result.Failure(CatalogErrors.NullSpecValues());
        }

        foreach (var key in values.Keys)
        {
            if (FindSpec(key) is null)
            {
                return Result.Failure(CatalogErrors.SpecUnknown(key));
            }
        }

        foreach (var spec in _specSchema)
        {
            if (!values.TryGetValue(spec.Key, out var value))
            {
                return Result.Failure(CatalogErrors.SpecMissing(spec.Key));
            }

            switch (spec.Type, value)
            {
                case (SpecType.Number, NumberValue):
                case (SpecType.Text, TextValue):
                case (SpecType.Boolean, BoolValue):
                    break;
                case (SpecType.Enum, TextValue text):
                    if (!spec.Options.Contains(text.Value))
                    {
                        return Result.Failure(CatalogErrors.SpecInvalidOption(spec.Key));
                    }
                    break;
                default:
                    return Result.Failure(CatalogErrors.SpecTypeMismatch(spec.Key));
            }
        }

        return Result.Success();
    }

    private static Error? ValidateSchema(IReadOnlyList<SpecDefinition> specSchema)
    {
        var seen = new HashSet<string>();
        foreach (var spec in specSchema)
        {
            if (spec is null)
            {
                return CatalogErrors.InvalidSpecSchema("Spec definitions must not be null.");
            }

            if (spec.Key is null || !SpecKeyPattern().IsMatch(spec.Key))
            {
                return CatalogErrors.InvalidSpecSchema($"Spec key '{spec.Key}' must match ^[a-z][a-z0-9_]*$.");
            }

            if (string.IsNullOrWhiteSpace(spec.Label))
            {
                return CatalogErrors.InvalidSpecSchema($"Spec '{spec.Key}' needs a label.");
            }

            if (spec.Options is null)
            {
                return CatalogErrors.InvalidSpecSchema($"Spec '{spec.Key}' must have an options list (empty if none).");
            }

            if (!seen.Add(spec.Key))
            {
                return CatalogErrors.InvalidSpecSchema($"Spec key '{spec.Key}' is duplicated.");
            }

            if (spec.Type == SpecType.Enum)
            {
                if (spec.Options.Count == 0)
                {
                    return CatalogErrors.InvalidSpecSchema($"Enum spec '{spec.Key}' needs at least one option.");
                }

                if (spec.Options.Any(string.IsNullOrWhiteSpace))
                {
                    return CatalogErrors.InvalidSpecSchema($"Enum spec '{spec.Key}' has a blank option.");
                }

                if (spec.Options.Any(o => o.Contains(',')))
                {
                    return CatalogErrors.InvalidSpecSchema($"Enum spec '{spec.Key}' has an option containing a comma.");
                }

                if (spec.Options.Distinct().Count() != spec.Options.Count)
                {
                    return CatalogErrors.InvalidSpecSchema($"Enum spec '{spec.Key}' has duplicate options.");
                }
            }
            else if (spec.Options.Count != 0)
            {
                return CatalogErrors.InvalidSpecSchema($"Spec '{spec.Key}' must not define options unless it is an Enum.");
            }
        }

        return null;
    }
}
