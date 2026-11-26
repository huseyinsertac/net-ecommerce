using Shop.SharedKernel;

namespace Basket.Domain.Baskets;

public sealed class Basket : AggregateRoot<Guid>
{
    public const int MaxQuantityPerLine = 10;
    public const int MaxLines = 20;

    private List<BasketLine> _lines = [];

    private Basket() { }

    public BasketOwner Owner { get; private set; } = null!;
    public IReadOnlyList<BasketLine> Lines => _lines.AsReadOnly();
    public DateTime UpdatedAtUtc { get; private set; }

    public static Basket Create(BasketOwner owner, DateTime nowUtc) =>
        new() { Id = Guid.CreateVersion7(), Owner = owner, UpdatedAtUtc = nowUtc };

    public Result SetQuantity(Guid variantId, int quantity, DateTime nowUtc)
    {
        if (quantity < 0 || quantity > MaxQuantityPerLine)
        {
            return Result.Failure(BasketErrors.InvalidQuantity(MaxQuantityPerLine));
        }

        var line = _lines.Find(l => l.VariantId == variantId);
        if (quantity == 0)
        {
            Remove(variantId, nowUtc);
            return Result.Success();
        }

        if (line is null)
        {
            if (_lines.Count >= MaxLines)
            {
                return Result.Failure(BasketErrors.TooManyLines(MaxLines));
            }

            _lines.Add(new BasketLine(variantId, quantity, nowUtc));
        }
        else
        {
            line.Quantity = quantity;
        }

        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public void Remove(Guid variantId, DateTime nowUtc)
    {
        if (_lines.RemoveAll(l => l.VariantId == variantId) > 0)
        {
            UpdatedAtUtc = nowUtc;
        }
    }

    public void Clear(DateTime nowUtc)
    {
        _lines.Clear();
        UpdatedAtUtc = nowUtc;
    }

    public void MergeFrom(Basket other, DateTime nowUtc)
    {
        if (ReferenceEquals(this, other) || Id == other.Id)
        {
            throw new InvalidOperationException("A basket cannot be merged into itself.");
        }

        foreach (var incoming in other._lines.OrderBy(l => l.AddedAtUtc))
        {
            var existing = _lines.Find(l => l.VariantId == incoming.VariantId);
            if (existing is not null)
            {
                existing.Quantity = Math.Min(MaxQuantityPerLine, existing.Quantity + incoming.Quantity);
            }
            else if (_lines.Count < MaxLines)
            {
                _lines.Add(new BasketLine(incoming.VariantId, Math.Min(MaxQuantityPerLine, incoming.Quantity),
                    incoming.AddedAtUtc));
            }
        }

        UpdatedAtUtc = nowUtc;
    }
}
