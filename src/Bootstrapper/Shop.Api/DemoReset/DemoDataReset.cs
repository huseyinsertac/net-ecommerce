using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using Npgsql;
using Shop.Infrastructure.Common.Persistence;

namespace Shop.Api.DemoReset;

/// <summary>
/// Wipes every module's data and re-seeds the demo. The live demo is public and shared, so this runs nightly
/// (<see cref="NightlyResetService"/>) and on demand (<see cref="DemoResetCommand"/>).
/// <para>
/// Safety: only the schemas of the module DbContexts in <see cref="ModuleDatabaseRegistry"/> are touched, never
/// <c>public</c> or anything else, and never the EF migrations history. Table and sequence names come from the database
/// catalog, filtered to those schemas; no name ever comes from input. The wipe is one transaction with a statement
/// and lock timeout: it either empties every module table or changes nothing.
/// </para>
/// <para>
/// A wipe that loses a lock race (lock timeout, deadlock, statement timeout) is rolled back and retried with backoff,
/// and so is a failed re-seed after the wipe has committed (seeders are idempotent).
/// </para>
/// </summary>
public sealed class DemoDataReset(
    IServiceProvider services,
    ModuleDatabaseRegistry registry,
    IConfiguration configuration,
    IOptions<DemoResetOptions> options,
    TimeProvider clock,
    ILogger<DemoDataReset> logger)
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    private static readonly string[] ForbiddenSchemas = ["public", "information_schema"];

    public async Task ResetAsync(CancellationToken ct)
    {
        var started = clock.GetTimestamp();
        var schemas = ModuleSchemas();

        var (tables, sequences) = await WithRetriesAsync(
            "Demo data wipe", IsTransientLockFailure, () => TruncateAsync(schemas, ct), ct);
        logger.LogInformation(
            "Demo reset truncated {Tables} tables and restarted {Sequences} sequences in schemas {Schemas}",
            tables, sequences, string.Join(", ", schemas));

        // The wipe is committed, so the demo stays broken until a seed succeeds; seeders are idempotent, so retry them.
        await WithRetriesAsync("Demo re-seed", _ => true, async () =>
        {
            await ModuleSeeding.RunAllAsync(services, logger, ct);
            return true;
        }, ct);

        logger.LogInformation("Demo reset finished in {ElapsedMs:0} ms", clock.GetElapsedTime(started).TotalMilliseconds);
    }

    /// <summary>Lock wait timeout, deadlock, or statement timeout: a concurrent transaction was in the way.</summary>
    private static bool IsTransientLockFailure(Exception ex) => ex is PostgresException
    {
        SqlState: PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.QueryCanceled,
    };

    /// <summary>
    /// Runs <paramref name="action"/> up to <c>MaxAttempts</c> times, waiting <c>RetryBackoff</c> (doubling) between
    /// attempts. Each retry is logged at Warning, the final failure at Error; the last exception propagates.
    /// </summary>
    private async Task<T> WithRetriesAsync<T>(
        string operation, Func<Exception, bool> isRetryable, Func<Task<T>> action, CancellationToken ct)
    {
        var maxAttempts = Math.Max(1, options.Value.MaxAttempts);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && isRetryable(ex) && attempt < maxAttempts)
            {
                var delay = options.Value.RetryBackoff * Math.Pow(2, attempt - 1);
                logger.LogWarning(ex, "{Operation} failed (attempt {Attempt}/{Max}); retrying in {DelayMs:0} ms",
                    operation, attempt, maxAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay, clock, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "{Operation} failed (attempt {Attempt}/{Max}); giving up", operation, attempt, maxAttempts);
                throw;
            }
        }
    }

    /// <summary>The schema of every registered module DbContext, validated as a plain lower-case identifier.</summary>
    private List<string> ModuleSchemas()
    {
        var schemas = new List<string>();
        foreach (var contextType in registry.ContextTypes)
        {
            using var scope = services.CreateScope();
            var schema = ((ModuleDbContext)scope.ServiceProvider.GetRequiredService(contextType)).Schema;
            if (!IsPlainIdentifier(schema)
                || ForbiddenSchemas.Contains(schema, StringComparer.Ordinal)
                || schema.StartsWith("pg_", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Refusing to reset schema '{schema}' of {contextType.Name}.");
            }

            if (!schemas.Contains(schema, StringComparer.Ordinal))
            {
                schemas.Add(schema);
            }
        }

        if (schemas.Count == 0)
        {
            throw new InvalidOperationException("No module DbContexts are registered; nothing to reset.");
        }

        return schemas;
    }

    private async Task<(int Tables, int Sequences)> TruncateAsync(List<string> schemas, CancellationToken ct)
    {
        var timeoutMs = (long)Math.Ceiling(options.Value.StatementTimeout.TotalMilliseconds);
        var lockTimeoutMs = (long)Math.Ceiling(options.Value.LockTimeout.TotalMilliseconds);
        if (timeoutMs < 1 || lockTimeoutMs < 1)
        {
            throw new InvalidOperationException("DemoReset:StatementTimeout and DemoReset:LockTimeout must be positive.");
        }

        var connectionString = configuration.GetConnectionString(ModuleDbContextRegistration.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ModuleDbContextRegistration.ConnectionStringName}' is not configured.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // Integers from configuration, formatted invariantly: SET does not take parameters.
        await ExecuteAsync(connection, transaction,
            $"SET LOCAL statement_timeout = {timeoutMs.ToString(CultureInfo.InvariantCulture)}", ct);
        await ExecuteAsync(connection, transaction,
            $"SET LOCAL lock_timeout = {lockTimeoutMs.ToString(CultureInfo.InvariantCulture)}", ct);

        var tables = await ReadNamesAsync(connection, transaction, """
            SELECT table_schema, table_name
            FROM information_schema.tables
            WHERE table_type = 'BASE TABLE' AND table_schema = ANY(@schemas) AND table_name <> @history
            ORDER BY table_schema, table_name
            """, schemas, ct);
        if (tables.Count == 0)
        {
            throw new InvalidOperationException("No module tables were found; has the database been migrated?");
        }

        var sequences = await ReadNamesAsync(connection, transaction, """
            SELECT sequence_schema, sequence_name
            FROM information_schema.sequences
            WHERE sequence_schema = ANY(@schemas)
            ORDER BY sequence_schema, sequence_name
            """, schemas, ct);

        // One statement, so every module table is emptied together. No CASCADE on purpose: every table of these schemas
        // is listed, so CASCADE could only ever reach a table outside them, and that must fail loudly, not be wiped.
        var truncate = new StringBuilder("TRUNCATE TABLE ");
        truncate.AppendJoin(", ", tables.Select(Qualified));
        truncate.Append(" RESTART IDENTITY");
        await ExecuteAsync(connection, transaction, truncate.ToString(), ct);

        foreach (var sequence in sequences)
        {
            await ExecuteAsync(connection, transaction, $"ALTER SEQUENCE {Qualified(sequence)} RESTART", ct);
        }

        await transaction.CommitAsync(ct);
        return (tables.Count, sequences.Count);
    }

    private static async Task<List<(string Schema, string Name)>> ReadNamesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, List<string> schemas, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("schemas", schemas.ToArray());
        if (sql.Contains("@history", StringComparison.Ordinal))
        {
            command.Parameters.AddWithValue("history", MigrationsHistoryTable);
        }

        await using var reader = await command.ExecuteReaderAsync(ct);
        var names = new List<(string, string)>();
        while (await reader.ReadAsync(ct))
        {
            var schema = reader.GetString(0);
            var name = reader.GetString(1);
            // Defence in depth: the query is already filtered to these schemas.
            if (!schemas.Contains(schema, StringComparer.Ordinal) || name == MigrationsHistoryTable)
            {
                throw new InvalidOperationException($"Unexpected relation '{schema}.{name}' in the reset list.");
            }

            names.Add((schema, name));
        }

        return names;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string Qualified((string Schema, string Name) relation) =>
        $"{QuoteIdentifier(relation.Schema)}.{QuoteIdentifier(relation.Name)}";

    private static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static bool IsPlainIdentifier(string value) =>
        value.Length is > 0 and <= 63
        && value[0] is >= 'a' and <= 'z'
        && value.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');
}
