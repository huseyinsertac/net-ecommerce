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
}
