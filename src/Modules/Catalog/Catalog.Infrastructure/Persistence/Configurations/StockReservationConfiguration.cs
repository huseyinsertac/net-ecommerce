using Catalog.Domain.Reservations;
using Catalog.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence.Configurations;

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> b)
    {
        b.ToTable("stock_reservations");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();

        b.Property(r => r.Lines)
            .HasField("_lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(new JsonListConverter<ReservationLine>(), JsonListConverter<ReservationLine>.Comparer)
            .HasColumnType("jsonb");

        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.CreatedAtUtc);
        b.Property(r => r.ClosedAtUtc);
    }
}
