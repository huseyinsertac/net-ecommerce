using Npgsql;
using Testcontainers.PostgreSql;

namespace Shop.Infrastructure.Common.IntegrationTests;

/// <summary>One Postgres container shared by every test in the collection; each test gets its own database.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates a fresh, empty database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync(CancellationToken ct)
    {
        var name = $"t_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync(ct);
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
