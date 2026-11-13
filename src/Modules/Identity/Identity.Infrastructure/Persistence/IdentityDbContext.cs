using Microsoft.EntityFrameworkCore;
using Shop.Infrastructure.Common.Events;
using Shop.Infrastructure.Common.Outbox;
using Shop.Infrastructure.Common.Persistence;

namespace Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options,
    IDomainEventDispatcher dispatcher,
    PendingIntegrationEvents pendingEvents) : ModuleDbContext(options, dispatcher, pendingEvents)
{
    public override string Schema => "identity";
}
