using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.SharedKernel;

namespace Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    /// <summary>Shadow property mapped to PostgreSQL's <c>xmin</c> system column (no real column is created).</summary>
    public const string RowVersion = "RowVersion";

    public void Configure(EntityTypeBuilder<ProductVariant> b)
    {
        b.ToTable("product_variants", t => t.HasCheckConstraint(
            "ck_product_variants_stock",
            "stock_reserved >= 0 AND stock_reserved <= stock_on_hand AND stock_on_hand >= 0"));
        b.HasKey(v => v.Id);
        b.Property(v => v.Id).ValueGeneratedNever();

        b.HasIndex(v => v.Sku).IsUnique();

        b.Property(v => v.Options)
            .HasField("_options")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(new JsonStringMapConverter(), JsonStringMapConverter.Comparer)
            .HasColumnType("jsonb");

        // Single currency (USD): only the amount is stored.
        b.Property(v => v.Price)
            .HasConversion(money => money.Amount, amount => Money.Usd(amount))
            .HasColumnType("numeric(12,2)");
        b.HasIndex(v => v.Price);

        b.Property(v => v.StockOnHand);
        b.Property(v => v.StockReserved);
        b.Ignore(v => v.Available);

        // Reservations only touch the variant row, so the optimistic concurrency token lives here.
        b.Property<uint>(RowVersion).IsRowVersion();
    }
}
