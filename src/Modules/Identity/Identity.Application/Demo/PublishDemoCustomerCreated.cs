using Identity.Contracts.Events;
using Identity.Domain.Users;
using Shop.Application.Abstractions.Events;

namespace Identity.Application.Demo;

/// <summary>
/// Turns the domain event into the integration event Ordering seeds the demo orders from. Runs inside the user's
/// SaveChanges, so the outbox row commits with the user, and a failed save (an email collision) leaves nothing behind:
/// the repository clears the change tracker, which drops the tracked outbox row.
/// </summary>
internal sealed class PublishDemoCustomerCreated(IEventPublisher publisher) : IDomainEventHandler<DemoCustomerCreatedDomainEvent>
{
    public Task Handle(DemoCustomerCreatedDomainEvent e, CancellationToken ct)
    {
        publisher.Publish(new DemoCustomerCreated(e.User.Id, e.User.Email.Value));
        return Task.CompletedTask;
    }
}
