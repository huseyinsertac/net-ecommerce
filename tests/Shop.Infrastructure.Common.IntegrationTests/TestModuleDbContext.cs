using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions.Events;
using Shop.Infrastructure.Common.Events;
using Shop.Infrastructure.Common.Outbox;
using Shop.Infrastructure.Common.Persistence;
using Shop.SharedKernel;

namespace Shop.Infrastructure.Common.IntegrationTests;

public sealed class TestModuleDbContext(
    DbContextOptions<TestModuleDbContext> options, IDomainEventDispatcher dispatcher, PendingIntegrationEvents pending)
    : ModuleDbContext(options, dispatcher, pending)
{
    public override string Schema => "test";

    public DbSet<TestAggregate> Aggregates => Set<TestAggregate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<TestAggregate>(b =>
        {
            b.ToTable("aggregates");
            b.HasKey(a => a.Id);
            b.Property(a => a.Name).HasMaxLength(100);
        });
    }
}

/// <summary>A second "module" with its own schema, for cross-module routing tests.</summary>
public sealed class TestModule2DbContext(
    DbContextOptions<TestModule2DbContext> options, IDomainEventDispatcher dispatcher, PendingIntegrationEvents pending)
    : ModuleDbContext(options, dispatcher, pending)
{
    public override string Schema => "test2";
}

/// <summary>Scoped marker: a distinct Id per DI scope.</summary>
public sealed class ScopeMarker
{
    public Guid Id { get; } = Guid.NewGuid();
}

public sealed class TestAggregate : AggregateRoot<Guid>
{
    private TestAggregate() { }

    public string Name { get; private set; } = "";

    public static TestAggregate Create(string name)
    {
        var aggregate = new TestAggregate { Id = Guid.CreateVersion7(), Name = name };
        aggregate.Raise(new TestAggregateCreated(aggregate.Id));
        return aggregate;
    }
}

public sealed record TestAggregateCreated(Guid AggregateId) : DomainEvent;

public sealed record TestEvent(string Payload) : IntegrationEvent;

/// <summary>Singleton spy shared by the test handlers.</summary>
public sealed class HandlerProbe
{
    private int _inFlight;
    private int _maxInFlight;

    public ConcurrentQueue<(string Handler, Guid EventId)> Calls { get; } = new();
    public ConcurrentDictionary<string, Guid> ScopeIds { get; } = new();
    public bool ThrowInDomainHandler { get; set; }
    public bool AlwaysThrow { get; set; }
    public int FailuresRemainingForB;
    public TimeSpan HandlerDelay { get; set; } = TimeSpan.Zero;
    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    public int CountFor(string handler) => Calls.Count(c => c.Handler == handler);

    public async Task Record(string handler, Guid eventId, CancellationToken ct)
    {
        var now = Interlocked.Increment(ref _inFlight);
        int seen;
        while (now > (seen = Volatile.Read(ref _maxInFlight)) && Interlocked.CompareExchange(ref _maxInFlight, now, seen) != seen) { }
        try
        {
            Calls.Enqueue((handler, eventId));
            if (HandlerDelay > TimeSpan.Zero)
            {
                await Task.Delay(HandlerDelay, ct);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}

/// <summary>Domain-event handler that turns the domain event into an integration event (lands in the outbox).</summary>
public sealed class PublishOnCreated(IEventPublisher publisher, HandlerProbe probe) : IDomainEventHandler<TestAggregateCreated>
{
    public Task Handle(TestAggregateCreated e, CancellationToken ct)
    {
        if (probe.ThrowInDomainHandler)
        {
            throw new InvalidOperationException("domain handler failed");
        }

        publisher.Publish(new TestEvent(e.AggregateId.ToString()));
        return Task.CompletedTask;
    }
}

public sealed class RecordingHandler(HandlerProbe probe) : IIntegrationEventHandler<TestEvent>
{
    public async Task Handle(TestEvent e, CancellationToken ct)
    {
        await probe.Record(nameof(RecordingHandler), e.Id, ct);
        if (probe.AlwaysThrow)
        {
            throw new InvalidOperationException("handler exploded");
        }
    }
}

public sealed class HandlerA(HandlerProbe probe) : IIntegrationEventHandler<TestEvent>
{
    public Task Handle(TestEvent e, CancellationToken ct) => probe.Record(nameof(HandlerA), e.Id, ct);
}

public sealed class HandlerB(HandlerProbe probe) : IIntegrationEventHandler<TestEvent>
{
    public async Task Handle(TestEvent e, CancellationToken ct)
    {
        await probe.Record(nameof(HandlerB), e.Id, ct);
        if (Interlocked.Decrement(ref probe.FailuresRemainingForB) >= 0)
        {
            throw new InvalidOperationException("B failed");
        }
    }
}

public sealed class Module1ScopeHandler(ScopeMarker marker, HandlerProbe probe) : IIntegrationEventHandler<TestEvent>
{
    public Task Handle(TestEvent e, CancellationToken ct)
    {
        probe.ScopeIds[nameof(Module1ScopeHandler)] = marker.Id;
        return Task.CompletedTask;
    }
}

public sealed class Module2ScopeHandler(ScopeMarker marker, HandlerProbe probe) : IIntegrationEventHandler<TestEvent>
{
    public Task Handle(TestEvent e, CancellationToken ct)
    {
        probe.ScopeIds[nameof(Module2ScopeHandler)] = marker.Id;
        return Task.CompletedTask;
    }
}

/// <summary>Builds a DI container wired exactly like a module, against a fresh database.</summary>
public static class TestHost
{
    public static Task<ServiceProvider> BuildAsync(
        PostgresFixture postgres, Action<IServiceCollection> configure, CancellationToken ct) =>
        BuildAsync(postgres, (s, _) => configure(s), ct);

    public static async Task<ServiceProvider> BuildAsync(
        PostgresFixture postgres, Action<IServiceCollection, IConfiguration> configure, CancellationToken ct)
    {
        var connectionString = await postgres.CreateDatabaseAsync(ct);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Shop"] = connectionString })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<HandlerProbe>();
        services.AddEventing(typeof(TestEvent).Assembly);
        services.AddModuleDbContext<TestModuleDbContext>(configuration);
        configure(services, configuration);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        // The database already exists (fixture); create each registered module's schema and tables.
        await using var scope = provider.CreateAsyncScope();
        foreach (var contextType in provider.GetRequiredService<ModuleDatabaseRegistry>().ContextTypes)
        {
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(ct);
        }

        return provider;
    }

    public static OutboxProcessor<TestModuleDbContext> CreateProcessor(IServiceProvider provider) =>
        ActivatorUtilities.CreateInstance<OutboxProcessor<TestModuleDbContext>>(provider);

    /// <summary>Publishes integration events through the module's IEventPublisher and saves them to the outbox.</summary>
    public static async Task<List<TestEvent>> EnqueueAsync(IServiceProvider provider, int count, CancellationToken ct)
    {
        var events = Enumerable.Range(0, count).Select(i => new TestEvent($"msg-{i}")).ToList();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        events.ForEach(publisher.Publish);
        await scope.ServiceProvider.GetRequiredService<TestModuleDbContext>().SaveChangesAsync(ct);
        return events;
    }

    public static async Task<List<OutboxMessage>> OutboxAsync(IServiceProvider provider, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestModuleDbContext>()
            .OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredOnUtc).ToListAsync(ct);
    }
}
