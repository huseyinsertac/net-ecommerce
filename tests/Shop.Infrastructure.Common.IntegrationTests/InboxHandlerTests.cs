using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions.Events;
using Shop.Infrastructure.Common.Events;
using Shop.Infrastructure.Common.Persistence;
using Shouldly;

namespace Shop.Infrastructure.Common.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class InboxHandlerTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SameMessageDeliveredTwice_InvokesInnerHandlerOnce()
    {
        await using var sp = await TestHost.BuildAsync(
            postgres, s => s.AddInboxHandler<TestEvent, HandlerA, TestModuleDbContext>(), Ct);
        var bus = sp.GetRequiredService<IEventBus>();
        var e = new TestEvent("dup");

        await bus.PublishAsync(e, Ct);
        await bus.PublishAsync(e, Ct);

        sp.GetRequiredService<HandlerProbe>().CountFor(nameof(HandlerA)).ShouldBe(1);
        await using var scope = sp.CreateAsyncScope();
        var inbox = await scope.ServiceProvider.GetRequiredService<TestModuleDbContext>().InboxMessages.ToListAsync(Ct);
        var row = inbox.ShouldHaveSingleItem();
        row.MessageId.ShouldBe(e.Id);
        row.Consumer.ShouldBe(typeof(HandlerA).FullName);
    }

    [Fact]
    public async Task OneOfTwoHandlersFails_RetryDoesNotReinvokeSucceededHandler()
    {
        await using var sp = await TestHost.BuildAsync(postgres, s => s
            .AddInboxHandler<TestEvent, HandlerA, TestModuleDbContext>()
            .AddInboxHandler<TestEvent, HandlerB, TestModuleDbContext>(), Ct);
        var probe = sp.GetRequiredService<HandlerProbe>();
        probe.FailuresRemainingForB = 1;
        await TestHost.EnqueueAsync(sp, 1, Ct);
        var processor = TestHost.CreateProcessor(sp);

        await processor.ProcessBatchAsync(Ct);
        await processor.ProcessBatchAsync(Ct);

        probe.CountFor(nameof(HandlerA)).ShouldBe(1);
        probe.CountFor(nameof(HandlerB)).ShouldBe(2);
        var row = (await TestHost.OutboxAsync(sp, Ct)).ShouldHaveSingleItem();
        row.ProcessedOnUtc.ShouldNotBeNull();
        row.Attempts.ShouldBe(1);
    }

    [Fact]
    public void AddInboxHandler_IsTheOnlyIntegrationHandlerRegistration()
    {
        var services = new ServiceCollection();
        services.AddInboxHandler<TestEvent, HandlerA, TestModuleDbContext>();
        services.AddInboxHandler<TestEvent, HandlerA, TestModuleDbContext>();

        var registrations = services.Where(d => d.ServiceType == typeof(IIntegrationEventHandler<TestEvent>)).ToList();
        registrations.ShouldHaveSingleItem().ImplementationType.ShouldBe(
            typeof(Inbox.InboxHandler<TestEvent, HandlerA, TestModuleDbContext>));
    }

    [Fact]
    public void AddInboxHandlers_WrapsEveryIntegrationHandlerInTheAssembly()
    {
        var services = new ServiceCollection();
        services.AddInboxHandlers<TestModuleDbContext>(typeof(TestEvent).Assembly);

        services.Where(d => d.ServiceType == typeof(IIntegrationEventHandler<TestEvent>))
            .Select(d => d.ImplementationType)
            .ShouldBe(
            [
                typeof(Inbox.InboxHandler<TestEvent, RecordingHandler, TestModuleDbContext>),
                typeof(Inbox.InboxHandler<TestEvent, HandlerA, TestModuleDbContext>),
                typeof(Inbox.InboxHandler<TestEvent, HandlerB, TestModuleDbContext>),
            ],
            ignoreOrder: true);
    }
}
