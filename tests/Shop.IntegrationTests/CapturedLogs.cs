using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Shop.IntegrationTests;

/// <summary>An in-memory logger provider, so a test can assert on what the host logged and at which level.</summary>
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyList<CapturedLog> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void Dispose()
    {
    }

    /// <summary>Waits until <paramref name="count"/> entries match, or fails after <paramref name="timeout"/>.</summary>
    public async Task<IReadOnlyList<CapturedLog>> WaitForAsync(
        Func<CapturedLog, bool> match, TimeSpan timeout, CancellationToken ct, int count = 1)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var matches = Entries.Where(match).ToList();
            if (matches.Count >= count)
            {
                return matches;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Expected {count} matching log entries, saw {matches.Count}.");
            }

            await Task.Delay(100, ct);
        }
    }

    private sealed class Logger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new CapturedLog(category, logLevel, formatter(state, exception)));
    }
}

public sealed record CapturedLog(string Category, LogLevel Level, string Message);
