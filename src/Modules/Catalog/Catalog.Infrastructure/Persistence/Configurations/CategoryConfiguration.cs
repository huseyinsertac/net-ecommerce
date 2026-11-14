using Catalog.Domain.Categories;
using Catalog.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("categories");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();

        b.Property(c => c.Slug).HasMaxLength(80);
        b.HasIndex(c => c.Slug).IsUnique();

        b.HasOne<Category>()
            .WithMany()
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Property(c => c.SpecSchema)
            .HasField("_specSchema")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(new JsonListConverter<SpecDefinition>(), JsonListConverter<SpecDefinition>.Comparer)
            .HasColumnType("jsonb");
    }
}
