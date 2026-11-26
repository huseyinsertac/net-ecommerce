using Basket.Application.Baskets;
using Basket.Domain.Baskets;
using Catalog.Contracts.Variants;
using Shouldly;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.UnitTests;

public class BasketViewTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static BasketAggregate BasketWith(params (Guid VariantId, int Quantity)[] lines)
    {
        var basket = BasketAggregate.Create(BasketOwner.ForAnonymous(Guid.NewGuid()), Now);
        var at = Now;
        foreach (var (variantId, quantity) in lines)
        {
            at = at.AddSeconds(1);
            basket.SetQuantity(variantId, quantity, at).IsSuccess.ShouldBeTrue();
        }

        return basket;
    }

    private static VariantSnapshot Snapshot(Guid id, decimal price, int available, bool purchasable = true) =>
        new(id, Guid.NewGuid(), "arcwing-14", "Arcwing 14 Laptop", "Arcwing", "AW14-SLV-512", "Silver · 512 GB",
            price, available, "/images/products/arcwing-14-front-400.webp", purchasable);

    [Fact]
    public void Build_NoBasket_Empty()
    {
        var view = BasketView.Build(null, new Dictionary<Guid, VariantSnapshot>());

        view.Lines.ShouldBeEmpty();
        view.ItemCount.ShouldBe(0);
        view.Subtotal.ShouldBe(0m);
        view.HasIssues.ShouldBeFalse();
    }

    [Fact]
    public void Build_PurchasableLine_LiveFieldsAndLineTotal()
    {
        var id = Guid.NewGuid();
        var basket = BasketWith((id, 2));
        var snapshot = Snapshot(id, 1099.99m, 5);

        var view = BasketView.Build(basket, new Dictionary<Guid, VariantSnapshot> { [id] = snapshot });

        var line = view.Lines.ShouldHaveSingleItem();
        line.VariantId.ShouldBe(id);
        line.ProductSlug.ShouldBe("arcwing-14");
        line.ProductName.ShouldBe("Arcwing 14 Laptop");
        line.Brand.ShouldBe("Arcwing");
        line.Sku.ShouldBe("AW14-SLV-512");
        line.OptionsLabel.ShouldBe("Silver · 512 GB");
        line.ThumbnailUrl.ShouldBe("/images/products/arcwing-14-front-400.webp");
        line.UnitPrice.ShouldBe(1099.99m);
        line.Quantity.ShouldBe(2);
        line.Available.ShouldBe(5);
        line.LineTotal.ShouldBe(2199.98m);
        line.Issue.ShouldBeNull();
        view.ItemCount.ShouldBe(2);
        view.Subtotal.ShouldBe(2199.98m);
        view.HasIssues.ShouldBeFalse();
    }

    [Fact]
    public void Unavailable_LineExcludedFromSubtotal()
    {
        var ok = Guid.NewGuid();
        var draft = Guid.NewGuid();
        var basket = BasketWith((ok, 1), (draft, 3));
        var snapshots = new Dictionary<Guid, VariantSnapshot>
        {
            [ok] = Snapshot(ok, 249m, 4),
            [draft] = Snapshot(draft, 99m, 10, purchasable: false),
        };

        var view = BasketView.Build(basket, snapshots);

        var line = view.Lines.Single(l => l.VariantId == draft);
        line.Issue.ShouldBe(BasketLineIssue.Unavailable);
        line.LineTotal.ShouldBeNull();
        line.Quantity.ShouldBe(3);
        view.Subtotal.ShouldBe(249m);
        view.ItemCount.ShouldBe(4);
        view.HasIssues.ShouldBeTrue();
    }

    [Fact]
    public void InsufficientStock_Flagged()
    {
        var id = Guid.NewGuid();
        var basket = BasketWith((id, 3));

        var view = BasketView.Build(basket, new Dictionary<Guid, VariantSnapshot> { [id] = Snapshot(id, 129m, 1) });

        var line = view.Lines.ShouldHaveSingleItem();
        line.Issue.ShouldBe(BasketLineIssue.InsufficientStock);
        line.Available.ShouldBe(1);
        line.UnitPrice.ShouldBe(129m); // still the live price
        line.LineTotal.ShouldBeNull();
        view.Subtotal.ShouldBe(0m);
        view.ItemCount.ShouldBe(3);
        view.HasIssues.ShouldBeTrue();
    }

    [Fact]
    public void MissingSnapshot_Unavailable_NoPrice()
    {
        var id = Guid.NewGuid();
        var basket = BasketWith((id, 2));

        var view = BasketView.Build(basket, new Dictionary<Guid, VariantSnapshot>());

        var line = view.Lines.ShouldHaveSingleItem();
        line.VariantId.ShouldBe(id);
        line.Quantity.ShouldBe(2);
        line.Issue.ShouldBe(BasketLineIssue.Unavailable);
        line.UnitPrice.ShouldBeNull();
        line.LineTotal.ShouldBeNull();
        line.ProductName.ShouldBeNull();
        line.Available.ShouldBe(0);
        view.Subtotal.ShouldBe(0m);
        view.HasIssues.ShouldBeTrue();
    }

    [Fact]
    public void Build_LinesInOrderAdded()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var basket = BasketWith((first, 1), (second, 1));

        var view = BasketView.Build(basket, new Dictionary<Guid, VariantSnapshot>());

        view.Lines.Select(l => l.VariantId).ShouldBe([first, second]);
    }
}
