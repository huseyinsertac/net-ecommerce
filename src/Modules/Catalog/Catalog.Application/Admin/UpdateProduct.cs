using Catalog.Application.Abstractions;
using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using FluentValidation;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Catalog.Application.Admin;

/// <summary>Replaces a product's details, specs and status, if it is still at <paramref name="Version"/>.</summary>
/// <param name="Status">"Draft", "Active" or "Archived".</param>
public sealed record UpdateProduct(
    Guid ProductId,
    string? Name,
    string? Brand,
    string? Description,
    bool IsFeatured,
    IReadOnlyList<string>? ImageKeys,
    IReadOnlyDictionary<string, SpecValue>? Specs,
    string? Status,
    uint? Version) : ICommand<AdminProductDto>;

internal sealed class UpdateProductValidator : AbstractValidator<UpdateProduct>
{
    public UpdateProductValidator()
    {
        AdminRules.Details(this, x => x.Name, x => x.Brand, x => x.Description, x => x.ImageKeys, x => x.Specs);
        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => s is null || AdminMapping.ParseStatus(s) is not null)
            .WithMessage("Status must be Draft, Active or Archived.");
        RuleFor(x => x.Version).NotNull();
    }
}

internal sealed class UpdateProductHandler(IProductRepository repository, IAdminCatalogReader reader)
    : ICommandHandler<UpdateProduct, AdminProductDto>
{
    public async Task<Result<AdminProductDto>> Handle(UpdateProduct command, CancellationToken ct)
    {
        var product = await repository.GetAsync(command.ProductId, ct);
        if (product is null)
        {
            return CatalogErrors.ProductNotFound(command.ProductId.ToString());
        }

        if (repository.VersionOf(product) != command.Version)
        {
            return CatalogErrors.ConcurrentUpdate();
        }

        if (await AdminRules.CheckImageKeysAsync(repository, command.ImageKeys!, ct) is { } imageError)
        {
            return imageError;
        }

        var category = await repository.GetCategoryAsync(product.CategoryId, ct)
            ?? throw new InvalidOperationException($"Product {product.Id} has no category {product.CategoryId}.");

        var changed = product.UpdateDetails(command.Name!, command.Brand!, command.Description!, command.IsFeatured, command.ImageKeys!);
        if (changed.IsSuccess)
        {
            changed = product.SetSpecs(category, command.Specs!);
        }

        if (changed.IsSuccess)
        {
            changed = product.SetStatus(AdminMapping.ParseStatus(command.Status)!.Value);
        }

        if (changed.IsFailure)
        {
            return changed.Error; // nothing is saved
        }

        return await AdminSave.SaveAndReadAsync(repository, reader, product, command.Version!.Value, ct);
    }
}

/// <summary>Sets one variant's price and stock, if the product is still at <paramref name="Version"/>.</summary>
public sealed record UpdateVariant(Guid ProductId, Guid VariantId, decimal? Price, int? StockOnHand, uint? Version)
    : ICommand<AdminProductDto>;

internal sealed class UpdateVariantValidator : AbstractValidator<UpdateVariant>
{
    public UpdateVariantValidator()
    {
        RuleFor(x => x.Price).Price();
        RuleFor(x => x.StockOnHand).Stock();
        RuleFor(x => x.Version).NotNull();
    }
}

internal sealed class UpdateVariantHandler(IProductRepository repository, IAdminCatalogReader reader)
    : ICommandHandler<UpdateVariant, AdminProductDto>
{
    public async Task<Result<AdminProductDto>> Handle(UpdateVariant command, CancellationToken ct)
    {
        var product = await repository.GetAsync(command.ProductId, ct);
        if (product is null)
        {
            return CatalogErrors.ProductNotFound(command.ProductId.ToString());
        }

        if (repository.VersionOf(product) != command.Version)
        {
            return CatalogErrors.ConcurrentUpdate();
        }

        var changed = product.SetVariantPrice(command.VariantId, Money.Usd(command.Price!.Value));
        if (changed.IsSuccess)
        {
            changed = product.SetVariantStock(command.VariantId, command.StockOnHand!.Value);
        }

        if (changed.IsFailure)
        {
            return changed.Error; // nothing is saved
        }

        return await AdminSave.SaveAndReadAsync(repository, reader, product, command.Version!.Value, ct);
    }
}

internal static class AdminSave
{
    /// <summary>Saves with the optimistic version check, then reads the product back with its new version.</summary>
    public static async Task<Result<AdminProductDto>> SaveAndReadAsync(
        IProductRepository repository, IAdminCatalogReader reader, Product product, uint version, CancellationToken ct)
    {
        if (await repository.SaveAsync(product, version, ct) != ProductSaveOutcome.Saved)
        {
            return CatalogErrors.ConcurrentUpdate();
        }

        return await reader.GetProductAsync(product.Id, ct) is { } saved
            ? saved
            : CatalogErrors.ProductNotFound(product.Id.ToString());
    }
}
