using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Shop.Infrastructure.Common.Events;
using Shop.Infrastructure.Common.Outbox;
using Shop.Infrastructure.Common.Persistence;

namespace Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options,
    IDomainEventDispatcher dispatcher,
    PendingIntegrationEvents pendingEvents,
    IntegrationEventTypeRegistry registry) : ModuleDbContext(options, dispatcher, pendingEvents, registry)
{
    public override string Schema => "identity";

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}
