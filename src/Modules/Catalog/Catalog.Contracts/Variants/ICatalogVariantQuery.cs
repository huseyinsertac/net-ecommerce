namespace Catalog.Contracts.Variants;

/// <summary>Live variant data for other modules (the basket). Catalog owns price, stock, naming and axis order.</summary>
public interface ICatalogVariantQuery
{
    /// <summary>
    /// Returns a snapshot per known variant id; unknown ids are absent. Non-active products are still returned
    /// (with <see cref="VariantSnapshot.IsPurchasable"/> false). At most <see cref="MaxIds"/> ids per call.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, VariantSnapshot>> GetAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken ct);

    const int MaxIds = 100;
}
