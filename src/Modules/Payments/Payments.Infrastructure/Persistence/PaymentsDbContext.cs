using Microsoft.EntityFrameworkCore;
using Payments.Domain.Payments;
using Payments.Infrastructure.Webhooks;
using Shop.Infrastructure.Common.Events;
using Shop.Infrastructure.Common.Outbox;
using Shop.Infrastructure.Common.Persistence;

namespace Payments.Infrastructure.Persistence;

public sealed class PaymentsDbContext(
    DbContextOptions<PaymentsDbContext> options,
    IDomainEventDispatcher dispatcher,
    PendingIntegrationEvents pendingEvents,
    IntegrationEventTypeRegistry registry) : ModuleDbContext(options, dispatcher, pendingEvents, registry)
{
    public override string Schema => "payments";

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents => Set<ProcessedWebhookEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
    }
}
