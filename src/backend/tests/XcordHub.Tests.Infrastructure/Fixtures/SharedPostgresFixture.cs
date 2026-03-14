using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using Xunit;

namespace XcordHub.Tests.Infrastructure.Fixtures;

/// <summary>
/// Shared PostgreSQL fixture for integration tests. Starts a single PostgreSQL container
/// for the entire test assembly and creates isolated databases per test class via
/// <see cref="CreateDatabaseAsync"/>. This avoids the overhead of spinning up a new
/// container for every test method (~3-5s each) while maintaining full isolation.
/// </summary>
public sealed class SharedPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("xcordhub_shared")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly ConcurrentDictionary<string, string> _createdDatabases = new();

    /// <summary>
    /// The admin connection string pointing at the shared container's default database.
    /// Use <see cref="CreateDatabaseAsync"/> to get a connection string for an isolated DB.
    /// </summary>
    public string AdminConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Console.Error.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] SharedPostgresFixture: starting container...");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await _container.StartAsync(cts.Token);
        Console.Error.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] SharedPostgresFixture: ready ({sw.ElapsedMilliseconds}ms)");
    }

    public async Task DisposeAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _container.DisposeAsync();
        Console.Error.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] SharedPostgresFixture: disposed ({sw.ElapsedMilliseconds}ms)");
    }

    /// <summary>
    /// Creates a new database on the shared container and returns a connection string for it.
    /// Idempotent: if the database was already created, returns the cached connection string.
    /// The database schema is initialized via <c>EnsureCreatedAsync</c> on first call only.
    /// </summary>
    public async Task<string> CreateDatabaseAsync(string databaseName, string encryptionKey)
    {
        // Fast path: database already created by a previous test method in the same class
        if (_createdDatabases.TryGetValue(databaseName, out var cached))
            return cached;

        // Create the database using the admin connection
        await using var adminConn = new NpgsqlConnection(AdminConnectionString);
        await adminConn.OpenAsync();
        await using var cmd = adminConn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await cmd.ExecuteNonQueryAsync();

        // Build connection string for the new database
        var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = databaseName
        };
        var connectionString = builder.ToString();

        // Initialize schema
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var db = new HubDbContext(options, new AesEncryptionService(encryptionKey));
        await db.Database.EnsureCreatedAsync();

        _createdDatabases[databaseName] = connectionString;
        return connectionString;
    }
}

[CollectionDefinition("SharedPostgres")]
public class SharedPostgresCollection : ICollectionFixture<SharedPostgresFixture> { }
