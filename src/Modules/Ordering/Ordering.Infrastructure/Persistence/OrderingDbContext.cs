using Microsoft.EntityFrameworkCore;
using Shop.Infrastructure.Common.Events;
using Shop.Infrastructure.Common.Persistence;
using Shop.Infrastructure.Common.Outbox;

namespace Ordering.Infrastructure.Persistence;

public sealed class OrderingDbContext(
    DbContextOptions<OrderingDbContext> options,
    IDomainEventDispatcher dispatcher,
    PendingIntegrationEvents pendingEvents) : ModuleDbContext(options, dispatcher, pendingEvents)
{
    public override string Schema => "ordering";
}
