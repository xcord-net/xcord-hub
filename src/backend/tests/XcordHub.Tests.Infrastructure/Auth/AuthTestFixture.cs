using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;
using Xunit;

namespace XcordHub.Tests.Infrastructure.Auth;

/// <summary>
/// xUnit collection fixture for the hub-auth integration test suite (cards 146).
/// Starts one PostgreSQL container and one Redis container for the entire
/// collection, exposing per-class isolated PG databases via
/// <see cref="CreateDatabaseAsync"/> and a single Redis multiplexer for all
/// auth handlers that need it (LoginHandler for brute-force tracking).
/// </summary>
public sealed class AuthIntegrationFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("xcordhub_auth_shared")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private readonly ConcurrentDictionary<string, string> _createdDatabases = new();

    public string AdminConnectionString => _postgres.GetConnectionString();
    public IConnectionMultiplexer Multiplexer { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Console.Error.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] AuthIntegrationFixture: starting containers...");
        var pgTask = _postgres.StartAsync();
        var redisTask = _redis.StartAsync();
        await Task.WhenAll(pgTask, redisTask);
        Multiplexer = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        Console.Error.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] AuthIntegrationFixture: ready ({sw.ElapsedMilliseconds}ms)");
    }

    public async Task DisposeAsync()
    {
        Multiplexer?.Dispose();
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Creates an isolated database on the shared Postgres container and applies
    /// the HubDbContext schema. Idempotent per database name.
    /// </summary>
    public async Task<string> CreateDatabaseAsync(string databaseName, string encryptionKey)
    {
        if (_createdDatabases.TryGetValue(databaseName, out var cached))
            return cached;

        await using var adminConn = new NpgsqlConnection(AdminConnectionString);
        await adminConn.OpenAsync();
        await using var cmd = adminConn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await cmd.ExecuteNonQueryAsync();

        var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = databaseName
        };
        var connectionString = builder.ToString();

        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var db = new HubDbContext(options, new AesEncryptionService(encryptionKey));
        await db.Database.EnsureCreatedAsync();

        _createdDatabases[databaseName] = connectionString;
        return connectionString;
    }
}

[CollectionDefinition("AuthIntegration")]
public class AuthIntegrationCollection : ICollectionFixture<AuthIntegrationFixture> { }

/// <summary>
/// Shared base for the hub auth integration test suite. Provides a DbContext
/// scoped to a per-class isolated PostgreSQL database, plus the shared Redis
/// multiplexer needed by LoginHandler.
/// </summary>
public abstract class AuthTestsBase
{
    protected const string TestEncryptionKey = "auth-handler-tests-encryption-key-256-bits-minimum-req!!!";
    protected const string RedisChannelPrefix = "auth-tests";

    protected readonly string _connectionString;
    protected readonly AuthIntegrationFixture _fixture;

    protected AuthTestsBase(AuthIntegrationFixture fixture, string dbName)
    {
        _fixture = fixture;
        _connectionString = fixture.CreateDatabaseAsync(dbName, TestEncryptionKey).GetAwaiter().GetResult();
    }

    protected HubDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
    }

    protected IConnectionMultiplexer Redis => _fixture.Multiplexer;

    protected static IOptions<RedisOptions> BuildRedisOptions() =>
        Options.Create(new RedisOptions { ChannelPrefix = RedisChannelPrefix });

    protected static IOptions<AuthOptions> BuildAuthOptions(
        int maxAttempts = 5,
        int windowMinutes = 15,
        int refreshDays = 30,
        int bcryptWorkFactor = 4) =>
        Options.Create(new AuthOptions
        {
            MaxLoginAttemptsPerWindow = maxAttempts,
            LoginAttemptWindowMinutes = windowMinutes,
            JwtRefreshTokenDays = refreshDays,
            BcryptWorkFactor = bcryptWorkFactor
        });

    /// <summary>
    /// Stub IHttpContextAccessor that returns null - LoginAttemptRecorder tolerates
    /// missing HttpContext and falls back to "unknown" IP / empty UA.
    /// </summary>
    protected static IHttpContextAccessor NullHttpContextAccessor() => new NullContextAccessor();

    private sealed class NullContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    /// <summary>
    /// Creates a HubUser with a BCrypt-hashed password (work factor 4 for speed).
    /// Returns the user, their plaintext email, and the plaintext password.
    /// </summary>
    protected async Task<(HubUser user, string email, string password)> SeedUserAsync(
        HubDbContext db,
        long userId,
        string usernameSuffix,
        bool twoFactorEnabled = false,
        bool isAdmin = false,
        bool isDisabled = false,
        string? twoFactorSecret = null)
    {
        var enc = new AesEncryptionService(TestEncryptionKey);
        var email = $"auth_{usernameSuffix}@test.invalid";
        var password = "TestPassword123!";
        var user = new HubUser
        {
            Id = userId,
            Username = $"au_{usernameSuffix}"[..Math.Min(32, $"au_{usernameSuffix}".Length)],
            DisplayName = $"AU {usernameSuffix}",
            Email = enc.Encrypt(email),
            EmailHash = enc.ComputeHmac(email.ToLowerInvariant()),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 4),
            IsAdmin = isAdmin,
            IsDisabled = isDisabled,
            TwoFactorEnabled = twoFactorEnabled,
            TwoFactorSecret = twoFactorSecret,
            CreatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = DateTimeOffset.UtcNow
        };
        db.HubUsers.Add(user);
        await db.SaveChangesAsync();
        return (user, email, password);
    }
}
