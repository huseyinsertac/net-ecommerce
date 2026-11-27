using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payments.Domain.Payments;
using Shop.SharedKernel;

namespace Payments.Infrastructure.Persistence;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    /// <summary>Shadow property mapped to PostgreSQL's <c>xmin</c> system column (no real column is created).</summary>
    public const string RowVersion = "RowVersion";

    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();

        // One payment per order: a concurrent second StartPayment loses here.
        b.HasIndex(p => p.OrderId).IsUnique();

        // Single currency (USD): only the amount is stored.
        b.Property(p => p.Amount).HasConversion(m => m.Amount, a => Money.Usd(a)).HasColumnType("numeric(12,2)");

        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.Provider).HasMaxLength(20);
        b.Property(p => p.ProviderSessionId).HasMaxLength(255);
        b.HasIndex(p => p.ProviderSessionId).IsUnique();
        b.Property(p => p.CheckoutUrl).HasMaxLength(2048);
        b.Property(p => p.FailureReason).HasMaxLength(200);

        // The expiry job's scan: Pending payments by deadline.
        b.HasIndex(p => p.ExpiresAtUtc).HasFilter("status = 'Pending'");

        b.Property<uint>(RowVersion).IsRowVersion();
    }
}
