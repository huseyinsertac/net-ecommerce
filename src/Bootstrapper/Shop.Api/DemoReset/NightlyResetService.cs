using Microsoft.Extensions.Options;

namespace Shop.Api.DemoReset;

/// <summary>
/// Runs <see cref="DemoDataReset"/> once a day at <c>DemoReset:TimeOfDayUtc</c>. Registered only when
/// <c>DemoReset:Enabled</c> is true. A failed reset is logged at Error and retried after <c>DemoReset:RetryDelay</c>, up
/// to <c>DemoReset:RetryCount</c> times; after that the service waits for the next day's slot.
/// </summary>
public sealed class NightlyResetService(
    DemoDataReset reset,
    IOptions<DemoResetOptions> options,
    TimeProvider clock,
    ILogger<NightlyResetService> logger) : BackgroundService
{
    /// <summary>When the next reset runs, and whether it retries a failed one.</summary>
    public sealed record Attempt(DateTimeOffset AtUtc, bool IsRetry);

    /// <summary>The first <paramref name="timeOfDayUtc"/> strictly after <paramref name="now"/>, in UTC.</summary>
    public static DateTimeOffset NextRunUtc(DateTimeOffset now, TimeSpan timeOfDayUtc)
    {
        var utc = now.ToUniversalTime();
        var today = new DateTimeOffset(utc.Date, TimeSpan.Zero) + timeOfDayUtc;
        return today > utc ? today : today.AddDays(1);
    }

    /// <summary>
    /// The next attempt: a retry <c>RetryDelay</c> from now while <paramref name="consecutiveFailures"/> is between 1 and
    /// <c>RetryCount</c>; otherwise the next daily slot after both <paramref name="now"/> and <paramref name="lastSlotUtc"/>
    /// (so a slot that already ran is never scheduled again, even if the timer fired a little early).
    /// </summary>
    public static Attempt ScheduleNext(
        DateTimeOffset now, DateTimeOffset lastSlotUtc, int consecutiveFailures, DemoResetOptions options)
    {
        if (consecutiveFailures >= 1 && consecutiveFailures <= options.RetryCount)
        {
            return new Attempt(now + options.RetryDelay, IsRetry: true);
        }

        return new Attempt(NextRunUtc(now > lastSlotUtc ? now : lastSlotUtc, options.TimeOfDayUtc), IsRetry: false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var lastSlot = DateTimeOffset.MinValue;
        var failures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = clock.GetUtcNow();
            var next = ScheduleNext(now, lastSlot, failures, settings);
            if (!next.IsRetry)
            {
                failures = 0;
                lastSlot = next.AtUtc;
            }

            logger.LogInformation("Next demo reset at {NextRunUtc:u}{Retry}", next.AtUtc, next.IsRetry ? $" (retry {failures})" : "");

            try
            {
                await Task.Delay(next.AtUtc - now, clock, stoppingToken);
                await reset.ResetAsync(stoppingToken);
                failures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                failures++;
                if (failures <= settings.RetryCount)
                {
                    logger.LogError(ex, "Nightly demo reset failed; retrying in {RetryDelay} ({Retry}/{RetryCount})",
                        settings.RetryDelay, failures, settings.RetryCount);
                }
                else
                {
                    logger.LogError(ex, "Nightly demo reset failed after {RetryCount} retries; the next attempt is tomorrow",
                        settings.RetryCount);
                }
            }
        }
    }
}
