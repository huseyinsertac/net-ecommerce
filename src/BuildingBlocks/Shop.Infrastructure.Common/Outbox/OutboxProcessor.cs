using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shop.Infrastructure.Common.Events;
using Shop.Infrastructure.Common.Persistence;
using Shop.SharedKernel;

namespace Shop.Infrastructure.Common.Outbox;

/// <summary>
/// Polls one module's outbox and hands each pending message to the <see cref="IEventBus"/>. Rows are claimed with
/// <c>FOR UPDATE SKIP LOCKED</c> inside a transaction that stays open for the whole batch, so several processors
/// (threads or app instances) never deliver the same row concurrently. Delivery is at-least-once: if the process dies
/// after a handler committed but before this transaction commits, the row is delivered again and the inbox dedupes it.
/// </summary>
public sealed class OutboxProcessor<TContext>(
    IServiceScopeFactory scopeFactory,
    IEventBus bus,
    IntegrationEventTypeRegistry registry,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<OutboxProcessor<TContext>> logger) : BackgroundService
    where TContext : ModuleDbContext
{
    private static readonly TimeSpan InitialFailureDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxFailureDelay = TimeSpan.FromSeconds(30);

    private readonly OutboxOptions _options = options.Value;

    /// <summary>Processes one batch. Returns the number of messages attempted (0 when nothing is pending).</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Schema comes from code (ModuleDbContext.Schema), never from input; values are parameterized.
#pragma warning disable EF1002
        var messages = await db.OutboxMessages
            .FromSqlRaw(
                $$"""
                 SELECT * FROM "{{db.Schema}}".outbox_messages
                 WHERE processed_on_utc IS NULL AND attempts < {0}
                 ORDER BY occurred_on_utc
                 LIMIT {1}
                 FOR UPDATE SKIP LOCKED
                 """,
                _options.MaxAttempts, _options.BatchSize)
            .ToListAsync(ct);
#pragma warning restore EF1002

        foreach (var message in messages)
        {
            await DeliverAsync(message, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return messages.Count;
    }

    private async Task DeliverAsync(OutboxMessage message, CancellationToken ct)
    {
        var type = registry.Resolve(message.Type);
        if (type is null)
        {
            message.Error = $"Unknown event type '{message.Type}'";
            message.Attempts = _options.MaxAttempts;
            logger.LogError("Outbox message {MessageId} in {Context} has unknown type {Type}; parked",
                message.Id, typeof(TContext).Name, message.Type);
            return;
        }

        try
        {
            var e = (IIntegrationEvent)(JsonSerializer.Deserialize(message.Content, type, EventSerializer.Options)
                ?? throw new InvalidOperationException($"Outbox message {message.Id} deserialized to null."));
            await bus.PublishAsync(e, ct);
            message.ProcessedOnUtc = clock.GetUtcNow().UtcDateTime;
            message.Error = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // shutdown is not a delivery failure; the transaction rolls back and the row stays pending
        }
        catch (Exception ex)
        {
            message.Attempts++;
            message.Error = ex.Message;
            if (message.Attempts >= _options.MaxAttempts)
            {
                logger.LogError(ex, "Outbox message {MessageId} ({Type}) in {Context} failed {Attempts} times; parked",
                    message.Id, message.Type, typeof(TContext).Name, message.Attempts);
            }
            else
            {
                logger.LogWarning(ex, "Outbox message {MessageId} ({Type}) in {Context} failed (attempt {Attempts})",
                    message.Id, message.Type, typeof(TContext).Name, message.Attempts);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PollingInterval, clock);
        var failureDelay = InitialFailureDelay;
        do
        {
            try
            {
                // Drain without waiting while full batches keep coming back.
                while (await ProcessBatchAsync(stoppingToken) == _options.BatchSize && !stoppingToken.IsCancellationRequested)
                {
                }

                failureDelay = InitialFailureDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The poll itself failed (e.g. database unreachable): back off exponentially instead of hammering it.
                logger.LogError(ex, "Outbox processing for {Context} failed; backing off {Delay}", typeof(TContext).Name, failureDelay);
                if (!await DelayAsync(failureDelay, stoppingToken))
                {
                    return;
                }

                failureDelay = TimeSpan.FromTicks(Math.Min(failureDelay.Ticks * 2, MaxFailureDelay.Ticks));
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private async Task<bool> DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, clock, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
