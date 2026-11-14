using Microsoft.EntityFrameworkCore;
using Shop.Infrastructure.Common.Events;
using Shop.Infrastructure.Common.Outbox;
using Shop.Infrastructure.Common.Persistence;

namespace Basket.Infrastructure.Persistence;

public sealed class BasketDbContext(
    DbContextOptions<BasketDbContext> options,
    IDomainEventDispatcher dispatcher,
    PendingIntegrationEvents pendingEvents,
    IntegrationEventTypeRegistry registry) : ModuleDbContext(options, dispatcher, pendingEvents, registry)
{
    public override string Schema => "basket";
}
