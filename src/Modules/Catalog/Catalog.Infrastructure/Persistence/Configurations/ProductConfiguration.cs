using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public const string SearchVector = "SearchVector";

    /// <summary>
    /// Shadow property mapped to PostgreSQL's <c>xmin</c> system column (no real column is created): the product's
    /// version for the admin's optimistic concurrency.
    /// </summary>
    public const string RowVersion = "RowVersion";

    /// <summary>
    /// Shadow property written on every admin save, so the product row (and so its <see cref="RowVersion"/>) changes even
    /// when only a variant was edited. Stock reservations touch only variant rows and leave the product's version alone.
    /// </summary>
    public const string UpdatedAtUtc = "UpdatedAtUtc";

    public const string SlugIndex = "ix_products_slug";

    public const string SearchVectorSql =
        "setweight(to_tsvector('english', coalesce(name,'')), 'A') || " +
        "setweight(to_tsvector('english', coalesce(brand,'')), 'B') || " +
        "setweight(to_tsvector('english', coalesce(description,'')), 'C')";

    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();

        b.Property(p => p.Slug).HasMaxLength(120);
        b.HasIndex(p => p.Slug).IsUnique();
        b.Property(p => p.Name).HasMaxLength(200);

        b.HasOne<Category>()
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.PrimitiveCollection(p => p.ImageKeys)
            .HasField("_imageKeys")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnType("text[]");

        b.Property(p => p.Specs)
            .HasField("_specs")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(new SpecValuesJsonConverter(), SpecValuesJsonConverter.Comparer)
            .HasColumnType("jsonb");

        b.Property(p => p.OptionAxes)
            .HasField("_optionAxes")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(new JsonListConverter<OptionAxis>(), JsonListConverter<OptionAxis>.Comparer)
            .HasColumnType("jsonb");

        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);

        b.HasMany(p => p.Variants)
            .WithOne()
            .HasForeignKey("ProductId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.Variants)
            .HasField("_variants")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Property<NpgsqlTsVector>(SearchVector)
            .HasComputedColumnSql(SearchVectorSql, stored: true);

        b.HasIndex(SearchVector).HasMethod("gin");
        b.HasIndex(p => p.Specs).HasMethod("gin").HasOperators("jsonb_path_ops");
        b.HasIndex(p => new { p.Status, p.CategoryId });

        b.Property<uint>(RowVersion).IsRowVersion();
        b.Property<DateTime?>(UpdatedAtUtc);
    }
}
