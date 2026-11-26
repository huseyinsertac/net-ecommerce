using Basket.Domain.Baskets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.Infrastructure.Persistence;

internal sealed class BasketConfiguration : IEntityTypeConfiguration<BasketAggregate>
{
    /// <summary>Shadow property mapped to PostgreSQL's <c>xmin</c> system column (no real column is created).</summary>
    public const string RowVersion = "RowVersion";

    public const string UserIndex = "ix_baskets_user_id";
    public const string AnonymousIndex = "ix_baskets_anonymous_id";

    public void Configure(EntityTypeBuilder<BasketAggregate> b)
    {
        b.ToTable("baskets", t => t.HasCheckConstraint(
            "ck_baskets_exactly_one_owner", "(user_id IS NULL) <> (anonymous_id IS NULL)"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        // The owner is a value object stored as two nullable columns in the baskets row. Owned (table-split) rather than a
        // complex property because EF Core cannot index complex-type columns, and each column needs a partial unique index:
        // one basket per user and one per anonymous cookie.
        b.OwnsOne(x => x.Owner, o =>
        {
            o.Property(x => x.UserId).HasColumnName("user_id");
            o.Property(x => x.AnonymousId).HasColumnName("anonymous_id");
            o.HasIndex(x => x.UserId).IsUnique().HasFilter("user_id IS NOT NULL").HasDatabaseName(UserIndex);
            o.HasIndex(x => x.AnonymousId).IsUnique().HasFilter("anonymous_id IS NOT NULL").HasDatabaseName(AnonymousIndex);
        });
        b.Navigation(x => x.Owner).IsRequired();

        b.Property(x => x.UpdatedAtUtc);

        b.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(BasketLineConfiguration.BasketId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines)
            .HasField("_lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Property<uint>(RowVersion).IsRowVersion();
    }
}

internal sealed class BasketLineConfiguration : IEntityTypeConfiguration<BasketLine>
{
    /// <summary>Shadow foreign key to the owning basket.</summary>
    public const string BasketId = "BasketId";

    public const string VariantIndex = "ix_basket_lines_basket_id_variant_id";

    public void Configure(EntityTypeBuilder<BasketLine> b)
    {
        b.ToTable("basket_lines");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property<Guid>(BasketId);
        b.HasIndex(BasketId, nameof(BasketLine.VariantId)).IsUnique().HasDatabaseName(VariantIndex);
    }
}
