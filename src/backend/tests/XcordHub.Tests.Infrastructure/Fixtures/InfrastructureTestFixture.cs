using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using Xunit;

namespace XcordHub.Tests.Infrastructure.Fixtures;

/// <summary>
/// Infrastructure test fixture that sets up Docker-in-Docker environment
/// with shared services (PostgreSQL, Redis, MinIO, Caddy).
/// Tests use this to verify the full provisioning pipeline end-to-end.
/// </summary>
public class InfrastructureTestFixture : IAsyncLifetime
{
    private IContainer? _dindContainer;
    private PostgreSqlContainer? _postgresContainer;
    private RedisContainer? _redisContainer;
    private IContainer? _minioContainer;
    private IContainer? _caddyContainer;

    public string PostgresConnectionString { get; private set; } = string.Empty;
    public string RedisConnectionString { get; private set; } = string.Empty;
    public string MinioEndpoint { get; private set; } = string.Empty;
    public string CaddyEndpoint { get; private set; } = string.Empty;
    public string DockerHost { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("XCORD_INFRA_TESTS") != "1")
        {
            Console.Error.WriteLine("[InfrastructureTestFixture] Skipped — set XCORD_INFRA_TESTS=1 to enable");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Console.Error.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] InfrastructureTestFixture: starting containers...");

        // Start Docker-in-Docker container
        // This provides an isolated Docker daemon for provisioning tests
        _dindContainer = new ContainerBuilder()
            .WithImage("docker:27-dind")
            .WithPrivileged(true)
            .WithPortBinding(2375, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(2375))
            .Build();

        await _dindContainer.StartAsync();

        // Get the DinD Docker socket endpoint
        var dindPort = _dindContainer.GetMappedPublicPort(2375);
        DockerHost = $"tcp://localhost:{dindPort}";
        Console.Error.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] InfrastructureTestFixture: DinD ready ({sw.ElapsedMilliseconds}ms)");

        // Start shared PostgreSQL for hub database
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("xcordhub_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        await _postgresContainer.StartAsync();
        PostgresConnectionString = _postgresContainer.GetConnectionString();
        Console.Error.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] InfrastructureTestFixture: Postgres ready ({sw.ElapsedMilliseconds}ms)");

        // Start shared Redis
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(6379))
            .Build();

        await _redisContainer.StartAsync();
        RedisConnectionString = _redisContainer.GetConnectionString();
        Console.Error.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] InfrastructureTestFixture: Redis ready ({sw.ElapsedMilliseconds}ms)");

        // Start MinIO for object storage
        _minioContainer = new ContainerBuilder()
            .WithImage("minio/minio:latest")
            .WithCommand("server", "/data")
            .WithEnvironment("MINIO_ROOT_USER", "minioadmin")
            .WithEnvironment("MINIO_ROOT_PASSWORD", "minioadmin")
            .WithPortBinding(9000, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(9000))
            .Build();

        await _minioContainer.StartAsync();
        var minioPort = _minioContainer.GetMappedPublicPort(9000);
        MinioEndpoint = $"localhost:{minioPort}";
        Console.Error.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] InfrastructureTestFixture: MinIO ready ({sw.ElapsedMilliseconds}ms)");

        // Start Caddy for reverse proxy
        _caddyContainer = new ContainerBuilder()
            .WithImage("caddy:2-alpine")
            .WithPortBinding(80, true)
            .WithPortBinding(443, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(80))
            .Build();

        await _caddyContainer.StartAsync();
        var caddyPort = _caddyContainer.GetMappedPublicPort(80);
        CaddyEndpoint = $"http://localhost:{caddyPort}";
        Console.Error.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] InfrastructureTestFixture: Caddy ready ({sw.ElapsedMilliseconds}ms)");

        // Run migrations on the hub database
        await RunMigrations();
        Console.Error.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] InfrastructureTestFixture: fully initialized ({sw.ElapsedMilliseconds}ms)");
    }

    public async Task DisposeAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // Dispose all containers in reverse order
        if (_caddyContainer != null)
            await _caddyContainer.DisposeAsync();

        if (_minioContainer != null)
            await _minioContainer.DisposeAsync();

        if (_redisContainer != null)
            await _redisContainer.DisposeAsync();

        if (_postgresContainer != null)
            await _postgresContainer.DisposeAsync();

        if (_dindContainer != null)
            await _dindContainer.DisposeAsync();
        Console.Error.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] InfrastructureTestFixture: disposed ({sw.ElapsedMilliseconds}ms)");
    }

    private const string TestEncryptionKey = "infra-test-encryption-key-with-256-bits-minimum-length-required!";

    public HubDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .UseLoggerFactory(LoggerFactory.Create(builder => builder.AddConsole()))
            .Options;

        return new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
    }

    public HubDbContext CreateFreshDbContext()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options;

        return new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
    }

    private async Task RunMigrations()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }
}

[CollectionDefinition("Infrastructure")]
public class InfrastructureCollection : ICollectionFixture<InfrastructureTestFixture> { }
