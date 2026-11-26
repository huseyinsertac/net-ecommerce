using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering.Domain.Orders;
using Shop.SharedKernel;

namespace Ordering.Infrastructure.Persistence;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    /// <summary>Shadow property mapped to PostgreSQL's <c>xmin</c> system column (no real column is created).</summary>
    public const string RowVersion = "RowVersion";

    /// <summary>One order per (customer, Idempotency-Key): the index a concurrent repeat of a checkout trips.</summary>
    public const string IdempotencyIndex = "ix_orders_customer_id_idempotency_key";

    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders");
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).ValueGeneratedNever();

        b.Property(o => o.OrderNumber).HasMaxLength(20);
        b.HasIndex(o => o.OrderNumber).IsUnique();

        b.Property(o => o.CustomerEmail).HasMaxLength(254);
        b.HasIndex(o => new { o.CustomerId, o.IdempotencyKey }).IsUnique().HasDatabaseName(IdempotencyIndex);
        // Order history: the customer's orders, newest first.
        b.HasIndex(o => new { o.CustomerId, o.PlacedAtUtc });

        b.Property(o => o.ShippingAddress)
            .HasConversion(new JsonColumnConverter<ShippingAddress>(), OrderingJson.Comparer<ShippingAddress>())
            .HasColumnType("jsonb");

        // Single currency (USD): only the amount is stored.
        b.Property(o => o.Subtotal).HasConversion(m => m.Amount, a => Money.Usd(a)).HasColumnType("numeric(12,2)");
        b.Property(o => o.Discount).HasConversion(m => m.Amount, a => Money.Usd(a)).HasColumnType("numeric(12,2)");
        b.Property(o => o.Total).HasConversion(m => m.Amount, a => Money.Usd(a)).HasColumnType("numeric(12,2)");

        b.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(o => o.CancellationReason).HasConversion<string>().HasMaxLength(20);

        b.Property(o => o.History)
            .HasField("_history")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                new JsonColumnConverter<IReadOnlyList<StatusChange>>(), OrderingJson.Comparer<IReadOnlyList<StatusChange>>())
            .HasColumnType("jsonb");

        b.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey(OrderLineConfiguration.OrderId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(o => o.Lines)
            .HasField("_lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Property<uint>(RowVersion).IsRowVersion();
    }
}

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    /// <summary>Shadow foreign key to the owning order.</summary>
    public const string OrderId = "OrderId";

    public void Configure(EntityTypeBuilder<OrderLine> b)
    {
        b.ToTable("order_lines");
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).ValueGeneratedNever();
        b.Property<Guid>(OrderId);
        b.HasIndex(OrderId, nameof(OrderLine.LineNumber)).IsUnique();

        b.Property(l => l.ProductName).HasMaxLength(200);
        b.Property(l => l.UnitPrice).HasConversion(m => m.Amount, a => Money.Usd(a)).HasColumnType("numeric(12,2)");
        b.Property(l => l.LineTotal).HasConversion(m => m.Amount, a => Money.Usd(a)).HasColumnType("numeric(12,2)");
    }
}
