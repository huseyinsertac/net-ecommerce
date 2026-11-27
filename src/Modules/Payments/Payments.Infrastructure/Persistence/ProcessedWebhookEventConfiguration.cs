using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payments.Infrastructure.Webhooks;

namespace Payments.Infrastructure.Persistence;

internal sealed class ProcessedWebhookEventConfiguration : IEntityTypeConfiguration<ProcessedWebhookEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedWebhookEvent> b)
    {
        b.ToTable("processed_webhook_events");
        b.HasKey(e => e.EventId);
        b.Property(e => e.EventId).HasColumnType("text");
    }
}
