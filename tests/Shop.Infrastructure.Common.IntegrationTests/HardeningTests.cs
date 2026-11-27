using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shop.Application.Abstractions.Events;
using Shop.Infrastructure.Common.Outbox;
using Shouldly;

namespace Shop.Infrastructure.Common.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class HardeningTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<ServiceProvider> Build(IReadOnlyDictionary<string, string?>? config = null) =>
        TestHost.BuildAsync(
            postgres, (s, _) => s.AddScoped<IDomainEventHandler<TestAggregateCreated>, PublishOnCreated>(), Ct, config);

    [Fact]
    public async Task SaveChanges_PublishingUnregisteredEventType_Throws()
    {
        await using var sp = await Build();
        await using var scope = sp.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IEventPublisher>().Publish(new GenericEvent<int>(1));

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<TestModuleDbContext>().SaveChangesAsync(Ct));

        ex.Message.ShouldContain("not registered");
        ex.Message.ShouldContain(typeof(GenericEvent<int>).FullName!);
    }

    [Fact]
    public async Task DomainDispatchThrows_PendingEventsCleared_RetrySavesNoStaleEvent()
    {
        await using var sp = await Build();
        var probe = sp.GetRequiredService<HandlerProbe>();
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
            scope.ServiceProvider.GetRequiredService<IEventPublisher>().Publish(new TestEvent("stale"));
            probe.ThrowInDomainHandler = true;
            db.Aggregates.Add(TestAggregate.Create("first"));
            await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync(Ct));

            probe.ThrowInDomainHandler = false;
            db.ChangeTracker.Clear();
            db.Aggregates.Add(TestAggregate.Create("second"));
            await db.SaveChangesAsync(Ct);
        }

        var outbox = await TestHost.OutboxAsync(sp, Ct);
        outbox.Count.ShouldBe(1);
        outbox[0].Content.ShouldNotContain("stale");
    }

    [Fact]
    public void PendingEvents_DisposedWithUndrained_LogsWarning()
    {
        var logger = new CapturingLogger();
        var pending = new PendingIntegrationEvents(logger);
        pending.Publish(new TestEvent("x"));

        pending.Dispose();

        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void PendingEvents_DisposedEmpty_DoesNotLog()
    {
        var logger = new CapturingLogger();
        new PendingIntegrationEvents(logger).Dispose();

        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task OutboxOptions_BoundFromConfiguration()
    {
        await using var sp = await Build(new Dictionary<string, string?> { ["Outbox:BatchSize"] = "7" });

        var options = sp.GetRequiredService<IOptions<OutboxOptions>>().Value;

        options.BatchSize.ShouldBe(7);
        options.MaxAttempts.ShouldBe(10);
    }

    private sealed class CapturingLogger : ILogger<PendingIntegrationEvents>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
