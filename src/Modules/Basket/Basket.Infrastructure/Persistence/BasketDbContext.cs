using Microsoft.EntityFrameworkCore;
using Shop.Infrastructure.Common.Events;
using Shop.Infrastructure.Common.Persistence;
using Shop.Infrastructure.Common.Outbox;

namespace Basket.Infrastructure.Persistence;

public sealed class BasketDbContext(
    DbContextOptions<BasketDbContext> options,
    IDomainEventDispatcher dispatcher,
    PendingIntegrationEvents pendingEvents) : ModuleDbContext(options, dispatcher, pendingEvents)
{
    public override string Schema => "basket";
}
