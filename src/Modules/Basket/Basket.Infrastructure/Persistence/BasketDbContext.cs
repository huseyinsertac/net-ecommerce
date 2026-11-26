using Microsoft.EntityFrameworkCore;
using Shop.Infrastructure.Common.Events;
using Shop.Infrastructure.Common.Outbox;
using Shop.Infrastructure.Common.Persistence;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.Infrastructure.Persistence;

public sealed class BasketDbContext(
    DbContextOptions<BasketDbContext> options,
    IDomainEventDispatcher dispatcher,
    PendingIntegrationEvents pendingEvents,
    IntegrationEventTypeRegistry registry) : ModuleDbContext(options, dispatcher, pendingEvents, registry)
{
    public override string Schema => "basket";

    public DbSet<BasketAggregate> Baskets => Set<BasketAggregate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BasketDbContext).Assembly);
    }
}
