using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions.Events;
using Shop.Infrastructure.Common.Outbox;
using Shop.Infrastructure.Common.Persistence;
using Shouldly;

namespace Shop.Infrastructure.Common.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class OutboxProcessorTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<ServiceProvider> BuildWithRecordingHandler(TestClock? clock = null, Action<OutboxOptions>? configure = null) =>
        TestHost.BuildAsync(postgres, s =>
        {
            s.AddScoped<IIntegrationEventHandler<TestEvent>, RecordingHandler>();
            if (clock is not null)
            {
                s.AddSingleton<TimeProvider>(clock);
            }

            if (configure is not null)
            {
                s.Configure(configure);
            }
        }, Ct);

    [Fact]
    public async Task ProcessBatch_PublishesPendingMessage_AndMarksProcessed()
    {
        await using var sp = await BuildWithRecordingHandler();
        var events = await TestHost.EnqueueAsync(sp, 1, Ct);

        var attempted = await TestHost.CreateProcessor(sp).ProcessBatchAsync(Ct);

        attempted.ShouldBe(1);
        var probe = sp.GetRequiredService<HandlerProbe>();
        probe.Calls.Select(c => c.EventId).ShouldBe([events[0].Id]);
        var row = (await TestHost.OutboxAsync(sp, Ct)).ShouldHaveSingleItem();
        row.Id.ShouldBe(events[0].Id);
        row.ProcessedOnUtc.ShouldNotBeNull();
        row.Error.ShouldBeNull();
    }

    [Fact]
    public async Task ProcessBatch_WhenHandlerThrows_IncrementsAttemptsAndRecordsError()
    {
        await using var sp = await BuildWithRecordingHandler();
        sp.GetRequiredService<HandlerProbe>().AlwaysThrow = true;
        await TestHost.EnqueueAsync(sp, 1, Ct);

        await TestHost.CreateProcessor(sp).ProcessBatchAsync(Ct);

        var row = (await TestHost.OutboxAsync(sp, Ct)).ShouldHaveSingleItem();
        row.Attempts.ShouldBe(1);
        row.Error.ShouldNotBeNull().ShouldContain("handler exploded");
        row.ProcessedOnUtc.ShouldBeNull();
    }

    [Fact]
    public async Task ProcessBatch_AfterMaxAttempts_StopsRetrying()
    {
        var clock = new TestClock();
        await using var sp = await BuildWithRecordingHandler(clock);
        var probe = sp.GetRequiredService<HandlerProbe>();
        probe.AlwaysThrow = true;
        await TestHost.EnqueueAsync(sp, 1, Ct);
        var processor = TestHost.CreateProcessor(sp);

        for (var i = 0; i < 11; i++)
        {
            await processor.ProcessBatchAsync(Ct);
            clock.Advance(TimeSpan.FromMinutes(5));
        }

        probe.Calls.Count.ShouldBe(new OutboxOptions().MaxAttempts);
        probe.Calls.Count.ShouldBe(10);
        var row = (await TestHost.OutboxAsync(sp, Ct)).ShouldHaveSingleItem();
        row.Attempts.ShouldBe(10);
        row.ProcessedOnUtc.ShouldBeNull();
        (await processor.ProcessBatchAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task ProcessBatch_UnknownType_IsParkedWithoutBlockingOthers()
    {
        await using var sp = await BuildWithRecordingHandler();
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                Type = "Gone.Event",
                Content = "{}",
                OccurredOnUtc = DateTime.UtcNow.AddMinutes(-1),
            });
            await db.SaveChangesAsync(Ct);
        }

        var valid = (await TestHost.EnqueueAsync(sp, 1, Ct))[0];

        await TestHost.CreateProcessor(sp).ProcessBatchAsync(Ct);

        var rows = await TestHost.OutboxAsync(sp, Ct);
        var processed = rows.Single(r => r.Id == valid.Id);
        processed.ProcessedOnUtc.ShouldNotBeNull();
        var parked = rows.Single(r => r.Type == "Gone.Event");
        parked.Attempts.ShouldBe(new OutboxOptions().MaxAttempts);
        parked.Error.ShouldNotBeNull().ShouldStartWith("Unknown event type");
        parked.ProcessedOnUtc.ShouldBeNull();
        sp.GetRequiredService<HandlerProbe>().Calls.Select(c => c.EventId).ShouldBe([valid.Id]);
    }

    [Fact]
    public async Task ConcurrentProcessors_DeliverEachMessageExactlyOnce()
    {
        // Raw (non-inbox) handler on purpose: the inbox would hide duplicate deliveries.
        await using var sp = await BuildWithRecordingHandler();
        var probe = sp.GetRequiredService<HandlerProbe>();
        probe.HandlerDelay = TimeSpan.FromMilliseconds(10);
        var events = await TestHost.EnqueueAsync(sp, 50, Ct);
        var first = TestHost.CreateProcessor(sp);
        var second = TestHost.CreateProcessor(sp);

        var rounds = 0;
        while (true)
        {
            var attempted = await Task.WhenAll(
                Task.Run(() => first.ProcessBatchAsync(Ct), Ct),
                Task.Run(() => second.ProcessBatchAsync(Ct), Ct));
            if (attempted.Sum() == 0)
            {
                break;
            }

            (++rounds).ShouldBeLessThan(20);
        }

        var delivered = probe.Calls.Select(c => c.EventId).ToList();
        delivered.Count.ShouldBe(50);
        delivered.Distinct().Count().ShouldBe(50);
        delivered.ShouldBe(events.Select(e => e.Id), ignoreOrder: true);
        probe.MaxInFlight.ShouldBeGreaterThan(1, "both processors should have been delivering at the same time");
        (await TestHost.OutboxAsync(sp, Ct)).ShouldAllBe(r => r.ProcessedOnUtc != null);
    }

    [Fact]
    public async Task Failed_message_is_not_retried_before_its_backoff_elapses()
    {
        var clock = new TestClock();
        await using var sp = await BuildWithRecordingHandler(clock);
        var probe = sp.GetRequiredService<HandlerProbe>();
        probe.AlwaysThrow = true;
        await TestHost.EnqueueAsync(sp, 1, Ct);
        var processor = TestHost.CreateProcessor(sp);

        (await processor.ProcessBatchAsync(Ct)).ShouldBe(1);
        probe.AlwaysThrow = false;

        (await processor.ProcessBatchAsync(Ct)).ShouldBe(0);
        clock.Advance(TimeSpan.FromSeconds(1));
        (await processor.ProcessBatchAsync(Ct)).ShouldBe(1);

        var row = (await TestHost.OutboxAsync(sp, Ct)).ShouldHaveSingleItem();
        row.Attempts.ShouldBe(1);
        row.ProcessedOnUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Backoff_doubles_and_is_capped()
    {
        var clock = new TestClock();
        await using var sp = await BuildWithRecordingHandler(clock);
        sp.GetRequiredService<HandlerProbe>().AlwaysThrow = true;
        await TestHost.EnqueueAsync(sp, 1, Ct);
        var processor = TestHost.CreateProcessor(sp);

        var lastFailureAt = clock.GetUtcNow().UtcDateTime;
        for (var i = 0; i < 3; i++)
        {
            lastFailureAt = clock.GetUtcNow().UtcDateTime;
            await processor.ProcessBatchAsync(Ct);
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        // Delays were 1 s, 2 s, 4 s.
        var row = (await TestHost.OutboxAsync(sp, Ct)).ShouldHaveSingleItem();
        row.Attempts.ShouldBe(3);
        (row.NextAttemptOnUtc!.Value - lastFailureAt).ShouldBe(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task Backoff_is_capped_at_RetryMaxDelay()
    {
        var clock = new TestClock();
        await using var sp = await BuildWithRecordingHandler(clock, o => o.RetryMaxDelay = TimeSpan.FromSeconds(3));
        sp.GetRequiredService<HandlerProbe>().AlwaysThrow = true;
        await TestHost.EnqueueAsync(sp, 1, Ct);
        var processor = TestHost.CreateProcessor(sp);

        var lastFailureAt = clock.GetUtcNow().UtcDateTime;
        for (var i = 0; i < 3; i++)
        {
            lastFailureAt = clock.GetUtcNow().UtcDateTime;
            await processor.ProcessBatchAsync(Ct);
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        var row = (await TestHost.OutboxAsync(sp, Ct)).ShouldHaveSingleItem();
        (row.NextAttemptOnUtc!.Value - lastFailureAt).ShouldBe(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Message_parks_after_MaxAttempts()
    {
        var clock = new TestClock();
        await using var sp = await BuildWithRecordingHandler(clock, o => o.MaxAttempts = 3);
        var probe = sp.GetRequiredService<HandlerProbe>();
        probe.AlwaysThrow = true;
        await TestHost.EnqueueAsync(sp, 1, Ct);
        var processor = TestHost.CreateProcessor(sp);

        for (var i = 0; i < 3; i++)
        {
            (await processor.ProcessBatchAsync(Ct)).ShouldBe(1);
            clock.Advance(TimeSpan.FromHours(1));
        }

        (await processor.ProcessBatchAsync(Ct)).ShouldBe(0);
        clock.Advance(TimeSpan.FromDays(1));
        (await processor.ProcessBatchAsync(Ct)).ShouldBe(0);
        probe.Calls.Count.ShouldBe(3);
        var row = (await TestHost.OutboxAsync(sp, Ct)).ShouldHaveSingleItem();
        row.Attempts.ShouldBe(3);
        row.Error.ShouldNotBeNull().ShouldContain("handler exploded");
        row.ProcessedOnUtc.ShouldBeNull();
    }
}
