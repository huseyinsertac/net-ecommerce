using Microsoft.EntityFrameworkCore;
using Shop.Infrastructure.Common.Events;
using Shop.Infrastructure.Common.Outbox;
using Shop.Infrastructure.Common.Persistence;

namespace Payments.Infrastructure.Persistence;

public sealed class PaymentsDbContext(
    DbContextOptions<PaymentsDbContext> options,
    IDomainEventDispatcher dispatcher,
    PendingIntegrationEvents pendingEvents) : ModuleDbContext(options, dispatcher, pendingEvents)
{
    public override string Schema => "payments";
}
