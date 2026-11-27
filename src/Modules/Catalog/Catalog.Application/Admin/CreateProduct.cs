using System.Text.RegularExpressions;
using Catalog.Application.Abstractions;
using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using FluentValidation;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Catalog.Application.Admin;

/// <param name="Name">Shown as the axis label; variants name their option values by it.</param>
/// <param name="Values">The values this axis offers; every variant picks one.</param>
public sealed record NewOptionAxisInput(string? Name, IReadOnlyList<string>? Values);

/// <param name="Options">Option axis name to the value this variant has on that axis.</param>
public sealed record NewVariantInput(string? Sku, IReadOnlyDictionary<string, string>? Options, decimal? Price, int? StockOnHand);

/// <summary>Creates a product as a Draft. The slug comes from the name, made unique with -2, -3 and so on.</summary>
public sealed record CreateProduct(
    string? CategorySlug,
    string? Name,
    string? Brand,
    string? Description,
    bool IsFeatured,
    IReadOnlyList<string>? ImageKeys,
    IReadOnlyDictionary<string, SpecValue>? Specs,
    IReadOnlyList<NewOptionAxisInput>? OptionAxes,
    IReadOnlyList<NewVariantInput>? Variants) : ICommand<CreatedProductDto>;

internal sealed class CreateProductValidator : AbstractValidator<CreateProduct>
{
    public const int MaxAxes = 3;
    public const int MaxAxisValues = 20;
    public const int MaxVariants = 50;
    public const int MaxOptionLength = 50;
    public const int MaxSkuLength = 64;

    public CreateProductValidator()
    {
        RuleFor(x => x.CategorySlug).NotEmpty().MaximumLength(80);
        AdminRules.Details(this, x => x.Name, x => x.Brand, x => x.Description, x => x.ImageKeys, x => x.Specs);

        RuleFor(x => x.OptionAxes)
            .NotNull()
            .Must(a => a is null || a.Count <= MaxAxes).WithMessage($"At most {MaxAxes} option axes are allowed.");
        RuleForEach(x => x.OptionAxes).NotNull().ChildRules(axis =>
        {
            axis.RuleFor(a => a.Name).NotEmpty().MaximumLength(MaxOptionLength);
            axis.RuleFor(a => a.Values)
                .NotEmpty()
                .Must(v => v is null || v.Count <= MaxAxisValues).WithMessage($"At most {MaxAxisValues} values are allowed.")
                .Must(v => v is null || v.All(value => !string.IsNullOrWhiteSpace(value) && value.Length <= MaxOptionLength))
                .WithMessage($"Values must be 1-{MaxOptionLength} characters.")
                .Must(v => v is null || v.Distinct().Count() == v.Count).WithMessage("Values must not repeat.");
        });

        RuleFor(x => x.Variants)
            .NotEmpty().WithMessage("At least one variant is required.")
            .Must(v => v is null || v.Count <= MaxVariants).WithMessage($"At most {MaxVariants} variants are allowed.");
        RuleForEach(x => x.Variants).NotNull().ChildRules(variant =>
        {
            variant.RuleFor(v => v.Sku).NotEmpty().MaximumLength(MaxSkuLength);
            variant.RuleFor(v => v.Options).NotNull();
            variant.RuleFor(v => v.Price).Price();
            variant.RuleFor(v => v.StockOnHand).Stock();
        });
    }
}

internal sealed partial class CreateProductHandler(IProductRepository repository, TimeProvider clock)
    : ICommandHandler<CreateProduct, CreatedProductDto>
{
    /// <summary>A concurrent create can take the chosen slug between the check and the insert; then pick again.</summary>
    private const int SlugAttempts = 3;

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRun();

    public async Task<Result<CreatedProductDto>> Handle(CreateProduct command, CancellationToken ct)
    {
        var category = await repository.GetCategoryBySlugAsync(command.CategorySlug!, ct);
        if (category is null)
        {
            return CatalogErrors.CategoryNotFound(command.CategorySlug!);
        }

        if (await repository.HasChildCategoriesAsync(category.Id, ct))
        {
            return CatalogErrors.InvalidProduct("Products can only be added to a leaf category.");
        }

        var axes = BuildAxes(command.OptionAxes!, out var axisError);
        if (axisError is not null)
        {
            return axisError;
        }

        var variants = BuildVariants(command.Variants!, command.OptionAxes!, axes, out var variantError);
        if (variantError is not null)
        {
            return variantError;
        }

        if (await AdminRules.CheckImageKeysAsync(repository, command.ImageKeys!, ct) is { } imageError)
        {
            return imageError;
        }

        var used = await repository.GetUsedSkusAsync([.. variants.Select(v => v.Sku)], ct);
        if (used.Count > 0)
        {
            return CatalogErrors.SkuInUse(used[0]);
        }

        var baseSlug = SlugFromName(command.Name!);
        for (var attempt = 1; ; attempt++)
        {
            var slug = await NextFreeSlugAsync(baseSlug, ct);
            var created = Product.Create(
                category, slug, command.Name!, command.Brand!, command.Description!, command.ImageKeys!, command.Specs!,
                axes, variants, command.IsFeatured, clock.GetUtcNow().UtcDateTime);
            if (created.IsFailure)
            {
                return created.Error;
            }

            repository.Add(created.Value);
            switch (await repository.SaveNewAsync(ct))
            {
                case ProductSaveOutcome.Saved:
                    return new CreatedProductDto(created.Value.Id, created.Value.Slug);
                case ProductSaveOutcome.DuplicateSlug when attempt < SlugAttempts:
                    continue;
                case ProductSaveOutcome.DuplicateSku:
                    return CatalogErrors.SkuInUse(string.Join(", ", variants.Select(v => v.Sku)));
                default:
                    return CatalogErrors.ConcurrentUpdate();
            }
        }
    }

    /// <summary>Lower-case, runs of anything but [a-z0-9] become '-', trimmed, cut to the slug length limit.</summary>
    internal static string SlugFromName(string name)
    {
        var slug = NonAlphanumericRun().Replace(name.ToLowerInvariant(), "-").Trim('-');
        return slug.Length <= Product.MaxSlugLength ? slug : slug[..Product.MaxSlugLength].TrimEnd('-');
    }

    /// <summary><paramref name="baseSlug"/> when free, otherwise the first free <c>-2</c>, <c>-3</c>... (still within the limit).</summary>
    private async Task<string> NextFreeSlugAsync(string baseSlug, CancellationToken ct)
    {
        if (baseSlug.Length == 0)
        {
            return baseSlug; // Product.Create reports catalog.invalid_slug
        }

        // Leave room for a "-NNNNNN" suffix.
        var root = baseSlug.Length <= Product.MaxSlugLength - 7 ? baseSlug : baseSlug[..(Product.MaxSlugLength - 7)].TrimEnd('-');
        var taken = (await repository.GetSlugsAsync(baseSlug, root + "-", ct)).ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(baseSlug))
        {
            return baseSlug;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{root}-{n}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Axis keys come from the names: "Storage size" becomes <c>storage_size</c>.</summary>
    private static List<OptionAxis> BuildAxes(IReadOnlyList<NewOptionAxisInput> inputs, out Error? error)
    {
        error = null;
        var axes = new List<OptionAxis>();
        foreach (var input in inputs)
        {
            var key = NonAlphanumericRun().Replace(input.Name!.Trim().ToLowerInvariant(), "_").Trim('_');
            if (key.Length == 0)
            {
                error = CatalogErrors.InvalidVariantOptions($"Option axis name '{input.Name}' needs a letter or a digit.");
                return axes;
            }

            if (axes.Any(a => a.Key == key))
            {
                error = CatalogErrors.InvalidVariantOptions($"Option axis '{input.Name}' is listed twice.");
                return axes;
            }

            axes.Add(new OptionAxis(key, input.Name.Trim()));
        }

        return axes;
    }

    /// <summary>Maps each variant's options from axis names to axis keys, checking the values against the axes.</summary>
    private static List<NewVariant> BuildVariants(
        IReadOnlyList<NewVariantInput> inputs, IReadOnlyList<NewOptionAxisInput> axisInputs, List<OptionAxis> axes,
        out Error? error)
    {
        error = null;
        var variants = new List<NewVariant>();
        foreach (var input in inputs)
        {
            var options = new Dictionary<string, string>();
            foreach (var (name, value) in input.Options!)
            {
                var index = axes.FindIndex(a => a.Label == name?.Trim());
                if (index < 0)
                {
                    error = CatalogErrors.InvalidVariantOptions($"SKU '{input.Sku}' names an unknown option axis '{name}'.");
                    return variants;
                }

                if (!axisInputs[index].Values!.Contains(value))
                {
                    error = CatalogErrors.InvalidVariantOptions(
                        $"SKU '{input.Sku}' has '{value}' for '{name}', which is not one of that axis's values.");
                    return variants;
                }

                options[axes[index].Key] = value;
            }

            variants.Add(new NewVariant(input.Sku!, options, Money.Usd(input.Price!.Value), input.StockOnHand!.Value));
        }

        return variants;
    }
}
