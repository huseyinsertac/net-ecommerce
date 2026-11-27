namespace Basket.Contracts;

/// <summary>One basket line as checkout sees it: which variant and how many.</summary>
public sealed record BasketItem(Guid VariantId, int Quantity);
