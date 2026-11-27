namespace Shop.Api.DemoReset;

/// <summary>The <c>DemoReset</c> section. Off by default; only the production compose file turns it on.</summary>
public sealed class DemoResetOptions
{
    public const string SectionName = "DemoReset";

    /// <summary>When true, <see cref="NightlyResetService"/> wipes and re-seeds the database every day.</summary>
    public bool Enabled { get; set; }

    /// <summary>The UTC time of day the nightly reset runs (default 02:00).</summary>
    public TimeSpan TimeOfDayUtc { get; set; } = TimeSpan.FromHours(2);

    /// <summary>Upper bound for each statement of the reset transaction, so a held lock fails the reset instead of hanging it.</summary>
    public TimeSpan StatementTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long the wipe waits for any one table lock. Short on purpose: an outbox batch in flight can hold a lock the
    /// wipe needs while its handler waits on a table the wipe already holds, a cycle Postgres cannot see. Giving up fast
    /// releases our locks, the batch finishes, and the retry goes through.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Attempts for the wipe transaction (on lock, deadlock or timeout errors) and, separately, for the re-seed.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Wait before the second attempt; it doubles each time (1, 2, 4, 8 s by default).</summary>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>After a failed nightly reset, wait this long before trying again.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Nightly retries after a failure before falling back to the next day's slot.</summary>
    public int RetryCount { get; set; } = 3;
}
