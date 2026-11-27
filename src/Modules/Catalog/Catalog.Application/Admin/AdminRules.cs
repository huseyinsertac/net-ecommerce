using System.Text.RegularExpressions;
using Catalog.Application.Abstractions;
using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using FluentValidation;
using Shop.SharedKernel;

namespace Catalog.Application.Admin;

/// <summary>
/// Request-shape limits shared by the admin commands, reported per field as <c>validation.failed</c>. The domain stays the
/// authority on the business rules; these bounds mostly keep requests small.
/// </summary>
internal static partial class AdminRules
{
    public const int MaxBrandLength = 100;
    public const int MaxDescriptionLength = 4000;
    public const int MaxImages = 12;
    public const int MaxImageKeyLength = 100;
    public const int MaxSpecs = 50;
    public const int MaxSpecTextLength = 200;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex ImageKeyPattern();

    public static void Details<T>(
        AbstractValidator<T> v,
        System.Linq.Expressions.Expression<Func<T, string?>> name,
        System.Linq.Expressions.Expression<Func<T, string?>> brand,
        System.Linq.Expressions.Expression<Func<T, string?>> description,
        System.Linq.Expressions.Expression<Func<T, IReadOnlyList<string>?>> imageKeys,
        System.Linq.Expressions.Expression<Func<T, IReadOnlyDictionary<string, SpecValue>?>> specs)
    {
        v.RuleFor(name).NotEmpty().MaximumLength(Product.MaxNameLength);
        v.RuleFor(brand).NotEmpty().MaximumLength(MaxBrandLength);
        v.RuleFor(description).NotEmpty().MaximumLength(MaxDescriptionLength);
        v.RuleFor(imageKeys)
            .NotEmpty().WithMessage("At least one image is required.")
            .Must(k => k is null || k.Count <= MaxImages).WithMessage($"At most {MaxImages} images are allowed.")
            .Must(k => k is null || k.All(key => key is not null && key.Length <= MaxImageKeyLength && ImageKeyPattern().IsMatch(key)))
            .WithMessage("Image keys must be lowercase words separated by hyphens.")
            .Must(k => k is null || k.Distinct().Count() == k.Count).WithMessage("Image keys must not repeat.");
        v.RuleFor(specs)
            .NotNull()
            .Must(s => s is null || s.Count <= MaxSpecs).WithMessage($"At most {MaxSpecs} specs are allowed.")
            .Must(s => s is null || s.Values.All(value => value is not TextValue t || t.Value.Length <= MaxSpecTextLength))
            .WithMessage($"Spec text values must be at most {MaxSpecTextLength} characters.");
    }

    public static IRuleBuilderOptions<T, decimal?> Price<T>(this IRuleBuilder<T, decimal?> rule) =>
        rule.NotNull()
            .InclusiveBetween(Product.MinPrice, Product.MaxPrice)
            .Must(p => p is null || decimal.Round(p.Value, 2) == p.Value).WithMessage("'{PropertyName}' must have at most 2 decimals.");

    public static IRuleBuilderOptions<T, int?> Stock<T>(this IRuleBuilder<T, int?> rule) =>
        rule.NotNull().InclusiveBetween(0, Product.MaxStock);

    /// <summary>The first of <paramref name="imageKeys"/> no product uses yet (so there is no photo for it), as an error.</summary>
    public static async Task<Error?> CheckImageKeysAsync(
        IProductRepository repository, IReadOnlyList<string> imageKeys, CancellationToken ct)
    {
        var known = (await repository.GetKnownImageKeysAsync([.. imageKeys.Distinct()], ct)).ToHashSet(StringComparer.Ordinal);
        var unknown = imageKeys.FirstOrDefault(k => !known.Contains(k));
        return unknown is null
            ? null
            : CatalogErrors.InvalidProduct($"Image '{unknown}' does not exist; pick one from the image list.");
    }
}
