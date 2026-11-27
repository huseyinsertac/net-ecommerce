using Microsoft.EntityFrameworkCore;
using Ordering.Domain.Orders;
using Shop.Infrastructure.Common.Events;
using Shop.Infrastructure.Common.Outbox;
using Shop.Infrastructure.Common.Persistence;

namespace Ordering.Infrastructure.Persistence;

public sealed class OrderingDbContext(
    DbContextOptions<OrderingDbContext> options,
    IDomainEventDispatcher dispatcher,
    PendingIntegrationEvents pendingEvents,
    IntegrationEventTypeRegistry registry) : ModuleDbContext(options, dispatcher, pendingEvents, registry)
{
    /// <summary>Source of order numbers (<c>VL-100001</c>, <c>VL-100002</c>, …), in this context's schema.</summary>
    public const string OrderNumberSequence = "order_number_seq";

    public const long FirstOrderNumber = 100001;

    public override string Schema => "ordering";

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasSequence<long>(OrderNumberSequence).StartsAt(FirstOrderNumber);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderingDbContext).Assembly);
    }
}
