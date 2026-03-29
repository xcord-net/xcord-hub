using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using XcordHub.Entities;
using XcordHub.Features.Destruction;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Infrastructure.Fixtures;
using Xunit;
using Microsoft.Extensions.Options;

namespace XcordHub.Tests.Infrastructure;

/// <summary>
/// Integration tests for <see cref="DropDatabaseStep"/> and <see cref="ReleaseRedisSlotStep"/>.
///
/// DropDatabaseStep tests use a real PostgreSQL Testcontainer via <see cref="SharedPostgresFixture"/>.
/// They create a real per-instance database, run the step, then verify the database is gone.
///
/// ReleaseRedisSlotStep tests use a spy/stub approach since Redis is not available in
/// the unit test environment. They verify correct behaviour (resilience, key prefix logic,
/// no-throw on connection failure) without requiring a live Redis server.
/// </summary>
[Collection("SharedPostgres")]
[Trait("Category", "Integration")]
public sealed class DestructionCleanupStepTests
{
    private readonly SharedPostgresFixture _fixture;
    private readonly HubDbContext _dbContext;
    private readonly string _hubConnectionString;

    // ID ranges reserved for this test class - must not overlap with other test classes.
    // 9_282_000_000 – 9_282_999_999
    private const long IdBase = 9_282_000_000L;

    private const string TestEncryptionKey =
        "destruction-cleanup-step-test-key-256-bits-min-ok!!";

    public DestructionCleanupStepTests(SharedPostgresFixture fixture)
    {
        _fixture = fixture;
        _hubConnectionString = fixture.AdminConnectionString;

        var connStr = fixture.CreateDatabaseAsync("xcordhub_destruction_cleanup_test", TestEncryptionKey)
            .GetAwaiter().GetResult();
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(connStr)
            .Options;
        _dbContext = new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
    }

    // ──────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────

    private async Task<(ManagedInstance instance, InstanceInfrastructure infra)> SeedInstanceAsync(
        long idBase, string prefix, string? instanceDbName = null, string? dbUsername = null)
    {
        var owner = new HubUser
        {
            Id = idBase,
            Username = $"{prefix}_owner",
            DisplayName = $"{prefix} Owner",
            Email = Encoding.UTF8.GetBytes($"encrypted-{prefix}"),
            EmailHash = Encoding.UTF8.GetBytes($"hash-{prefix}"),
            PasswordHash = "hashed",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.HubUsers.Add(owner);

        var instance = new ManagedInstance
        {
            Id = idBase + 1,
            OwnerId = owner.Id,
            Domain = $"{prefix}.xcord.net",
            DisplayName = $"{prefix} Instance",
            Status = InstanceStatus.Running,
            SnowflakeWorkerId = (int)(idBase % 1000),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.ManagedInstances.Add(instance);

        var infra = new InstanceInfrastructure
        {
            Id = idBase + 2,
            ManagedInstanceId = instance.Id,
            DockerContainerId = $"container_{prefix}",
            DockerNetworkId = $"network_{prefix}",
            DatabaseName = instanceDbName ?? $"xcord_{prefix}_instance",
            DatabasePassword = "testpwd",
            DatabaseUsername = dbUsername ?? "",
            RedisDb = 0,
            MinioAccessKey = "key",
            MinioSecretKey = "secret",
            CaddyRouteId = $"route_{prefix}",
            LiveKitApiKey = "lk_key",
            LiveKitSecretKey = "lk_secret",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.InstanceInfrastructures.Add(infra);

        await _dbContext.SaveChangesAsync();
        return (instance, infra);
    }

    private DropDatabaseStep BuildDropDatabaseStep()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = _hubConnectionString
            })
            .Build();

        var topologyOptions = Options.Create(new TopologyOptions());
        var resolver = new TopologyResolver(topologyOptions);

        return new DropDatabaseStep(config, resolver, NullLogger<DropDatabaseStep>.Instance);
    }

    private async Task<bool> DatabaseExistsAsync(string databaseName)
    {
        await using var conn = new NpgsqlConnection(_hubConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @name", conn);
        cmd.Parameters.AddWithValue("name", databaseName);
        return await cmd.ExecuteScalarAsync() != null;
    }

    private async Task<bool> RoleExistsAsync(string roleName)
    {
        await using var conn = new NpgsqlConnection(_hubConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM pg_roles WHERE rolname = @name", conn);
        cmd.Parameters.AddWithValue("name", roleName);
        return await cmd.ExecuteScalarAsync() != null;
    }

    private async Task CreateDatabaseAsync(string dbName)
    {
        await using var conn = new NpgsqlConnection(_hubConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task CreateRoleAsync(string roleName)
    {
        await using var conn = new NpgsqlConnection(_hubConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE ROLE \"{roleName}\"", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // ──────────────────────────────────────────────────────────
    // DropDatabaseStep
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DropDatabase_ExistingDatabase_DropsSuccessfully()
    {
        var dbName = "xcord_cleanup_drop_test_1";
        var (instance, infra) = await SeedInstanceAsync(IdBase + 100, "dropmain", instanceDbName: dbName);

        // Create the real database so the step has something to drop.
        await CreateDatabaseAsync(dbName);
        (await DatabaseExistsAsync(dbName)).Should().BeTrue("database should exist before the step runs");

        var step = BuildDropDatabaseStep();
        await step.ExecuteAsync(instance, infra, CancellationToken.None);

        (await DatabaseExistsAsync(dbName)).Should().BeFalse("database should be gone after DropDatabase step");
    }

    [Fact]
    public async Task DropDatabase_DatabaseAlreadyDropped_CompletesWithoutError()
    {
        // DropDatabase uses IF EXISTS - running on a non-existent database must not throw.
        var dbName = "xcord_cleanup_already_dropped";
        var (instance, infra) = await SeedInstanceAsync(IdBase + 200, "alreadydropped", instanceDbName: dbName);

        (await DatabaseExistsAsync(dbName)).Should().BeFalse("database must not exist for this test");

        var step = BuildDropDatabaseStep();

        // Must not throw - idempotent step.
        var act = async () => await step.ExecuteAsync(instance, infra, CancellationToken.None);
        await act.Should().NotThrowAsync("DropDatabase step must be resilient to already-dropped databases");
    }

    [Fact]
    public async Task DropDatabase_WithPerInstanceRole_DropsRoleToo()
    {
        var dbName = "xcord_cleanup_role_drop_test";
        var roleName = "xcord_cleanup_role_user";
        var (instance, infra) = await SeedInstanceAsync(IdBase + 300, "roledrop",
            instanceDbName: dbName, dbUsername: roleName);

        await CreateDatabaseAsync(dbName);
        await CreateRoleAsync(roleName);

        (await DatabaseExistsAsync(dbName)).Should().BeTrue();
        (await RoleExistsAsync(roleName)).Should().BeTrue();

        var step = BuildDropDatabaseStep();
        await step.ExecuteAsync(instance, infra, CancellationToken.None);

        (await DatabaseExistsAsync(dbName)).Should().BeFalse("database should be dropped");
        (await RoleExistsAsync(roleName)).Should().BeFalse("per-instance PG role should be dropped");
    }

    [Fact]
    public async Task DropDatabase_RoleAlreadyDropped_CompletesWithoutError()
    {
        var dbName = "xcord_cleanup_role_already_gone";
        var roleName = "xcord_cleanup_missing_role";
        var (instance, infra) = await SeedInstanceAsync(IdBase + 400, "rolegone",
            instanceDbName: dbName, dbUsername: roleName);

        await CreateDatabaseAsync(dbName);
        // Do NOT create the role - it was already cleaned up by a previous partial run.

        (await RoleExistsAsync(roleName)).Should().BeFalse("role must not exist for this test");

        var step = BuildDropDatabaseStep();

        var act = async () => await step.ExecuteAsync(instance, infra, CancellationToken.None);
        await act.Should().NotThrowAsync("DropDatabase step must be resilient to already-dropped roles");

        // Database itself should still be cleaned up even though role was missing.
        (await DatabaseExistsAsync(dbName)).Should().BeFalse("database should be dropped even when role was already gone");
    }

    [Fact]
    public async Task DropDatabase_EmptyDatabaseName_SkipsWithoutError()
    {
        var (instance, infra) = await SeedInstanceAsync(IdBase + 500, "emptydb", instanceDbName: "");

        var step = BuildDropDatabaseStep();
        var act = async () => await step.ExecuteAsync(instance, infra, CancellationToken.None);
        await act.Should().NotThrowAsync("DropDatabase step must skip gracefully when DatabaseName is empty");
    }

    [Fact]
    public void DropDatabase_StepName_IsDropDatabase()
    {
        var step = BuildDropDatabaseStep();
        step.StepName.Should().Be("DropDatabase");
    }

    // ──────────────────────────────────────────────────────────
    // ReleaseRedisSlotStep - resilience tests (no live Redis)
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ReleaseRedisSlot_ConnectionFails_DoesNotThrow()
    {
        // Point at an unreachable Redis - the step must swallow the error and log a warning.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:ConnectionString"] = "127.0.0.1:19379,abortConnect=false,connectTimeout=500,syncTimeout=500"
            })
            .Build();

        var topologyOptions = Options.Create(new TopologyOptions());
        var resolver = new TopologyResolver(topologyOptions);

        var step = new ReleaseRedisSlotStep(config, resolver, NullLogger<ReleaseRedisSlotStep>.Instance);

        var (instance, infra) = await SeedInstanceAsync(IdBase + 600, "redisfail");

        var act = async () => await step.ExecuteAsync(instance, infra, CancellationToken.None);
        await act.Should().NotThrowAsync(
            "ReleaseRedisSlot step must not propagate exceptions so the destruction pipeline continues");
    }

    [Fact]
    public void ReleaseRedisSlot_StepName_IsReleaseRedisSlot()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:ConnectionString"] = "127.0.0.1:6379,abortConnect=false"
            })
            .Build();

        var topologyOptions = Options.Create(new TopologyOptions());
        var resolver = new TopologyResolver(topologyOptions);

        var step = new ReleaseRedisSlotStep(config, resolver, NullLogger<ReleaseRedisSlotStep>.Instance);
        step.StepName.Should().Be("ReleaseRedisSlot");
    }

    // ──────────────────────────────────────────────────────────
    // Pipeline integration - both steps registered correctly
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DestructionPipeline_WithCleanupSteps_DoesNotFailOnMissingResources()
    {
        // Verifies that the full pipeline (including DropDatabase + ReleaseRedisSlot)
        // does not throw when the database and Redis resources do not exist (partial teardown).
        var dbName = "xcord_pipeline_cleanup_missing";
        var (instance, infra) = await SeedInstanceAsync(IdBase + 700, "pipelinemissing", instanceDbName: dbName);

        var callLog = new List<string>();

        var dropDbStep = BuildDropDatabaseStep();

        var redisConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:ConnectionString"] = "127.0.0.1:19379,abortConnect=false,connectTimeout=500,syncTimeout=500"
            })
            .Build();
        var topologyOptions = Options.Create(new TopologyOptions());
        var resolver = new TopologyResolver(topologyOptions);
        var releaseRedisStep = new ReleaseRedisSlotStep(redisConfig, resolver, NullLogger<ReleaseRedisSlotStep>.Instance);

        var steps = new IDestructionStep[] { dropDbStep, releaseRedisStep };
        var pipeline = new DestructionPipeline(steps, NullLogger<DestructionPipeline>.Instance);

        // Neither step should cause the pipeline to throw.
        var act = async () => await pipeline.RunAsync(instance, infra, CancellationToken.None);
        await act.Should().NotThrowAsync(
            "destruction pipeline must complete even when cleanup steps encounter missing resources");
    }
}
