using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <summary>Shadow property mapped to PostgreSQL's <c>xmin</c> system column (no real column is created).</summary>
    public const string RowVersion = "RowVersion";

    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(u => u.Id);
        b.Property(u => u.Id).ValueGeneratedNever();

        // Email is a value object: both the address as entered and its upper-invariant copy are stored, so lookups and
        // the case-insensitive uniqueness rule run on a plain indexed column. Owned (table-split into users) rather than a
        // complex property because EF Core cannot index complex-type columns.
        b.OwnsOne(u => u.Email, e =>
        {
            e.Property(x => x.Value).HasColumnName("email").HasMaxLength(254).IsRequired();
            e.Property(x => x.Normalized).HasColumnName("email_normalized").HasMaxLength(254).IsRequired();
            e.HasIndex(x => x.Normalized).IsUnique().HasDatabaseName("ix_users_email_normalized");
        });
        b.Navigation(u => u.Email).IsRequired();

        b.Property(u => u.DisplayName).HasMaxLength(80);
        b.Property(u => u.PasswordHash).HasMaxLength(512);

        b.PrimitiveCollection(u => u.Roles)
            .HasField("_roles")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnType("text[]");

        b.Ignore(u => u.CanLogInWithPassword);

        b.HasMany(u => u.RefreshTokens)
            .WithOne()
            .HasForeignKey(t => t.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(u => u.RefreshTokens)
            .HasField("_refreshTokens")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Property<uint>(RowVersion).IsRowVersion();
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).ValueGeneratedNever();

        b.Property(t => t.TokenHash).HasMaxLength(128);
        b.HasIndex(t => t.TokenHash).IsUnique();
        b.HasIndex(t => t.FamilyId);
    }
}
