using Microsoft.Extensions.Options;

namespace Shop.Api.DemoReset;

/// <summary>
/// Runs <see cref="DemoDataReset"/> once a day at <c>DemoReset:TimeOfDayUtc</c>. Registered only when
/// <c>DemoReset:Enabled</c> is true. A failed reset is logged at Error and the service waits for the next day.
/// </summary>
public sealed class NightlyResetService(
    DemoDataReset reset,
    IOptions<DemoResetOptions> options,
    TimeProvider clock,
    ILogger<NightlyResetService> logger) : BackgroundService
{
    /// <summary>The first <paramref name="timeOfDayUtc"/> strictly after <paramref name="now"/>, in UTC.</summary>
    public static DateTimeOffset NextRunUtc(DateTimeOffset now, TimeSpan timeOfDayUtc)
    {
        var utc = now.ToUniversalTime();
        var today = new DateTimeOffset(utc.Date, TimeSpan.Zero) + timeOfDayUtc;
        return today > utc ? today : today.AddDays(1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timeOfDay = options.Value.TimeOfDayUtc;
        var lastRun = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = clock.GetUtcNow();
            // Never schedule the same slot twice, even if the timer fired a little early and the reset was quick.
            var next = NextRunUtc(now > lastRun ? now : lastRun, timeOfDay);
            logger.LogInformation("Next demo reset at {NextRunUtc:u}", next);

            try
            {
                await Task.Delay(next - now, clock, stoppingToken);
                lastRun = next;
                await reset.ResetAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Nightly demo reset failed; the next attempt is tomorrow");
            }
        }
    }
}
