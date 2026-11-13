using Microsoft.EntityFrameworkCore;
using Shop.Infrastructure.Common.Events;
using Shop.Infrastructure.Common.Inbox;
using Shop.Infrastructure.Common.Outbox;
using Shop.SharedKernel;

namespace Shop.Infrastructure.Common.Persistence;

/// <summary>
/// Base DbContext for a module: owns the module's schema plus its outbox and inbox tables, and dispatches domain events
/// raised by tracked aggregates inside SaveChanges, before anything is written, so handler side effects (including
/// outbox rows) commit or roll back together with the aggregate.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options, IDomainEventDispatcher dispatcher) : DbContext(options)
{
    /// <summary>Guards against handlers that keep raising events forever.</summary>
    private const int MaxDispatchRounds = 10;

    public abstract string Schema { get; }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Schema is an instance member, so the per-module migrations history table is configured here rather than at
        // registration. UseNpgsql without a connection string amends the already-registered Npgsql options in place.
        optionsBuilder.UseNpgsql(o => o.MigrationsHistoryTable("__ef_migrations_history", Schema));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(m => m.Type).HasColumnName("type").HasMaxLength(500);
            b.Property(m => m.Content).HasColumnName("content").HasColumnType("jsonb");
            b.Property(m => m.OccurredOnUtc).HasColumnName("occurred_on_utc");
            b.Property(m => m.ProcessedOnUtc).HasColumnName("processed_on_utc");
            b.Property(m => m.Attempts).HasColumnName("attempts");
            b.Property(m => m.Error).HasColumnName("error");
            // Matches the processor's polling query (pending rows ordered by occurrence).
            b.HasIndex(m => m.OccurredOnUtc).HasFilter("processed_on_utc IS NULL");
        });

        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("inbox_messages");
            b.HasKey(m => new { m.MessageId, m.Consumer });
            b.Property(m => m.MessageId).HasColumnName("message_id");
            b.Property(m => m.Consumer).HasColumnName("consumer").HasMaxLength(500);
            b.Property(m => m.ProcessedOnUtc).HasColumnName("processed_on_utc");
        });
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await DispatchDomainEventsAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Synchronous saves would skip domain-event dispatch, so they are not allowed.</summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException($"Use {nameof(SaveChangesAsync)}; domain events are dispatched asynchronously.");

    private async Task DispatchDomainEventsAsync(CancellationToken ct)
    {
        for (var round = 0; ; round++)
        {
            var events = ChangeTracker.Entries<IHasDomainEvents>()
                .Select(entry => entry.Entity)
                .Where(entity => entity.DomainEvents.Count > 0)
                .ToList()
                .SelectMany(entity =>
                {
                    var raised = entity.DomainEvents.ToList();
                    entity.ClearDomainEvents();
                    return raised;
                })
                .ToList();

            if (events.Count == 0)
            {
                return;
            }

            if (round >= MaxDispatchRounds)
            {
                throw new InvalidOperationException(
                    $"Domain events were still being raised after {MaxDispatchRounds} dispatch rounds; check for handler cycles.");
            }

            await dispatcher.DispatchAsync(events, ct);
        }
    }
}
