using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using XcordHub.Entities;
using XcordHub.Features.Provisioning;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Tests.Infrastructure;

/// <summary>
/// Integration tests for ResolvePlacementStep — verifies that hub provisioning
/// correctly places instances into compute pools and resolves data pools.
/// Uses real PostgreSQL via Testcontainers.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ResolvePlacementTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private HubDbContext? _dbContext;
    private long _nextId = 9_300_000_000L;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("xcordhub_placement_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _dbContext = new HubDbContext(options, new AesEncryptionService("placement-test-encryption-key-with-256-bits-minimum-ok!"));
        await _dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dbContext != null) await _dbContext.DisposeAsync();
        if (_postgres != null) await _postgres.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // Scenario 2: Hub provisioning properly computes when to add data servers
    // -------------------------------------------------------------------------

    /// <summary>
    /// When data pools exist in TopologyOptions, ResolvePlacementStep should
    /// set PlacedInDataPool to the matching data pool.
    /// </summary>
    [Fact]
    public async Task Execute_WithSingleDataPool_SetsPlacedInDataPool()
    {
        var topoOptions = CreateTopologyOptions(
            computePools: [new ComputePoolConfig { Name = "free-pool", Tier = "free", Capacity = new() { TenantSlots = 100 } }],
            dataPools: [new DataPoolConfig { Name = "data-1", Database = new() { ConnectionString = "Host=db;Database=xcord" } }]);

        var step = CreateStep(topoOptions);
        var instanceId = await SeedPendingInstance(InstanceTier.Free);

        var result = await step.ExecuteAsync(instanceId);

        result.IsSuccess.Should().BeTrue();
        var infra = await _dbContext!.InstanceInfrastructures.FirstAsync(i => i.ManagedInstanceId == instanceId);
        infra.PlacedInDataPool.Should().Be("data-1");
    }

    /// <summary>
    /// When multiple data pools exist, the one matching the compute pool name is selected.
    /// </summary>
    [Fact]
    public async Task Execute_WithMultipleDataPools_MatchesByComputePoolName()
    {
        var topoOptions = CreateTopologyOptions(
            computePools: [new ComputePoolConfig { Name = "free-pool", Tier = "free", Capacity = new() { TenantSlots = 100 } }],
            dataPools:
            [
                new DataPoolConfig { Name = "pro-pool", Database = new() { ConnectionString = "Host=db-pro" } },
                new DataPoolConfig { Name = "free-pool", Database = new() { ConnectionString = "Host=db-free" } }
            ]);

        var step = CreateStep(topoOptions);
        var instanceId = await SeedPendingInstance(InstanceTier.Free);

        var result = await step.ExecuteAsync(instanceId);

        result.IsSuccess.Should().BeTrue();
        var infra = await _dbContext!.InstanceInfrastructures.FirstAsync(i => i.ManagedInstanceId == instanceId);
        infra.PlacedInDataPool.Should().Be("free-pool");
    }

    /// <summary>
    /// When no data pools are configured, PlacedInDataPool remains empty —
    /// data services are co-located on the compute pool.
    /// </summary>
    [Fact]
    public async Task Execute_WithNoDataPools_LeavesDataPoolEmpty()
    {
        var topoOptions = CreateTopologyOptions(
            computePools: [new ComputePoolConfig { Name = "free-pool", Tier = "free", Capacity = new() { TenantSlots = 100 } }],
            dataPools: []);

        var step = CreateStep(topoOptions);
        var instanceId = await SeedPendingInstance(InstanceTier.Free);

        var result = await step.ExecuteAsync(instanceId);

        result.IsSuccess.Should().BeTrue();
        var infra = await _dbContext!.InstanceInfrastructures.FirstAsync(i => i.ManagedInstanceId == instanceId);
        infra.PlacedInDataPool.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Scenario 3: Hub provisioning properly computes when to add fed servers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Free tier instance is placed in the free compute pool.
    /// </summary>
    [Fact]
    public async Task Execute_FreeTier_PlacesInFreePool()
    {
        var topoOptions = CreateTopologyOptions(
            computePools:
            [
                new ComputePoolConfig { Name = "free-pool", Tier = "free", Capacity = new() { TenantSlots = 50 } },
                new ComputePoolConfig { Name = "pro-pool", Tier = "pro", Capacity = new() { TenantSlots = 20 } }
            ]);

        var step = CreateStep(topoOptions);
        var instanceId = await SeedPendingInstance(InstanceTier.Free);

        var result = await step.ExecuteAsync(instanceId);

        result.IsSuccess.Should().BeTrue();
        var infra = await _dbContext!.InstanceInfrastructures.FirstAsync(i => i.ManagedInstanceId == instanceId);
        infra.PlacedInPool.Should().Be("free-pool");
    }

    /// <summary>
    /// Pro tier instance maps to "pro" tier and is placed in the pro pool.
    /// </summary>
    [Fact]
    public async Task Execute_ProTier_PlacesInProPool()
    {
        var topoOptions = CreateTopologyOptions(
            computePools:
            [
                new ComputePoolConfig { Name = "free-pool", Tier = "free", Capacity = new() { TenantSlots = 50 } },
                new ComputePoolConfig { Name = "pro-pool", Tier = "pro", Capacity = new() { TenantSlots = 20 } }
            ]);

        var step = CreateStep(topoOptions);
        var instanceId = await SeedPendingInstance(InstanceTier.Pro);

        var result = await step.ExecuteAsync(instanceId);

        result.IsSuccess.Should().BeTrue();
        var infra = await _dbContext!.InstanceInfrastructures.FirstAsync(i => i.ManagedInstanceId == instanceId);
        infra.PlacedInPool.Should().Be("pro-pool");
    }

    /// <summary>
    /// When the matching pool is at capacity, placement returns an error.
    /// Current behavior: no cross-tier fallback on capacity — only when no pool exists for the tier.
    /// </summary>
    [Fact]
    public async Task Execute_FreePoolAtCapacity_ReturnsCapacityError()
    {
        var topoOptions = CreateTopologyOptions(
            computePools:
            [
                new ComputePoolConfig { Name = "free-pool", Tier = "free", Capacity = new() { TenantSlots = 1 } },
                new ComputePoolConfig { Name = "basic-pool", Tier = "basic", Capacity = new() { TenantSlots = 50 } }
            ]);

        var step = CreateStep(topoOptions);

        // Fill the free pool to capacity
        var existingId = await SeedPendingInstance(InstanceTier.Free);
        var fillResult = await step.ExecuteAsync(existingId);
        fillResult.IsSuccess.Should().BeTrue("should fill the free pool");

        // Another free-tier instance — pool is full, returns error (no cross-tier fallback)
        var newInstanceId = await SeedPendingInstance(InstanceTier.Free);
        var result = await step.ExecuteAsync(newInstanceId);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Contain("CAPACITY");
    }

    /// <summary>
    /// When no pool exists for the requested tier, placement falls back to the next tier up.
    /// e.g. Basic tier, but no "basic" pool exists → falls back to "pro" pool.
    /// </summary>
    [Fact]
    public async Task Execute_NoPoolForTier_FallsBackToNextTierUp()
    {
        var topoOptions = CreateTopologyOptions(
            computePools:
            [
                // No "basic" pool — Basic tier should fall back to "pro"
                new ComputePoolConfig { Name = "pro-pool", Tier = "pro", Capacity = new() { TenantSlots = 50 } }
            ]);

        var step = CreateStep(topoOptions);
        var instanceId = await SeedPendingInstance(InstanceTier.Basic);

        var result = await step.ExecuteAsync(instanceId);

        result.IsSuccess.Should().BeTrue();
        var infra = await _dbContext!.InstanceInfrastructures.FirstAsync(i => i.ManagedInstanceId == instanceId);
        infra.PlacedInPool.Should().Be("pro-pool");
    }

    /// <summary>
    /// When all pools are at capacity, placement fails with an error.
    /// </summary>
    [Fact]
    public async Task Execute_AllPoolsAtCapacity_ReturnsError()
    {
        var topoOptions = CreateTopologyOptions(
            computePools:
            [
                new ComputePoolConfig { Name = "free-pool", Tier = "free", Capacity = new() { TenantSlots = 1 } }
            ]);

        var step = CreateStep(topoOptions);

        // Fill the only pool
        var existingId = await SeedPendingInstance(InstanceTier.Free);
        var fillResult = await step.ExecuteAsync(existingId);
        fillResult.IsSuccess.Should().BeTrue();

        // Try another — no fallback available
        var newInstanceId = await SeedPendingInstance(InstanceTier.Free);
        var result = await step.ExecuteAsync(newInstanceId);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Contain("CAPACITY");
    }

    /// <summary>
    /// Enterprise tier maps to "enterprise", placing on a dedicated host instead of a pool.
    /// </summary>
    [Fact]
    public async Task Execute_EnterpriseTier_PlacesOnDedicatedHost()
    {
        var topoOptions = CreateTopologyOptions(
            computePools: [new ComputePoolConfig { Name = "free-pool", Tier = "free", Capacity = new() { TenantSlots = 50 } }],
            dedicatedHosts: [new DedicatedHostConfig { Id = "ded-1", Tier = "enterprise" }]);

        var step = CreateStep(topoOptions);
        var instanceId = await SeedPendingInstance(InstanceTier.Enterprise);

        var result = await step.ExecuteAsync(instanceId);

        result.IsSuccess.Should().BeTrue();
        var infra = await _dbContext!.InstanceInfrastructures.FirstAsync(i => i.ManagedInstanceId == instanceId);
        infra.PlacedInPool.Should().Be("dedicated:ded-1");
    }

    /// <summary>
    /// When topology is not configured at all, instance goes to "default" pool.
    /// </summary>
    [Fact]
    public async Task Execute_NoTopologyConfigured_PlacesInDefault()
    {
        var topoOptions = new TopologyOptions(); // empty — IsConfigured = false
        var step = CreateStep(topoOptions);
        var instanceId = await SeedPendingInstance(InstanceTier.Free);

        var result = await step.ExecuteAsync(instanceId);

        result.IsSuccess.Should().BeTrue();
        var infra = await _dbContext!.InstanceInfrastructures.FirstAsync(i => i.ManagedInstanceId == instanceId);
        infra.PlacedInPool.Should().Be("default");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private ResolvePlacementStep CreateStep(TopologyOptions topoOptions)
    {
        var resolver = new TopologyResolver(Options.Create(topoOptions));
        return new ResolvePlacementStep(_dbContext!, resolver);
    }

    private static TopologyOptions CreateTopologyOptions(
        List<ComputePoolConfig>? computePools = null,
        List<DataPoolConfig>? dataPools = null,
        List<DedicatedHostConfig>? dedicatedHosts = null)
    {
        return new TopologyOptions
        {
            ComputePools = computePools ?? [],
            DataPools = dataPools ?? [],
            DedicatedHosts = dedicatedHosts ?? []
        };
    }

    private async Task<long> SeedPendingInstance(InstanceTier tier, bool mediaEnabled = false)
    {
        var id = Interlocked.Add(ref _nextId, 10);
        var db = _dbContext!;

        var owner = new HubUser
        {
            Id = id + 1,
            Username = $"user_{id}",
            DisplayName = $"User {id}",
            Email = Encoding.UTF8.GetBytes($"encrypted-{id}"),
            EmailHash = Encoding.UTF8.GetBytes($"hash-{id}"),
            PasswordHash = "hashed",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.HubUsers.Add(owner);

        var instance = new ManagedInstance
        {
            Id = id + 2,
            OwnerId = owner.Id,
            Domain = $"inst-{id}.xcord.net",
            DisplayName = $"Instance {id}",
            Status = InstanceStatus.Provisioning,
            SnowflakeWorkerId = (int)(id % 1000),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ManagedInstances.Add(instance);

        var billing = new InstanceBilling
        {
            Id = id + 3,
            ManagedInstanceId = instance.Id,
            Tier = tier,
            MediaEnabled = mediaEnabled,
            BillingStatus = BillingStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Set<InstanceBilling>().Add(billing);

        var infra = new InstanceInfrastructure
        {
            Id = id + 4,
            ManagedInstanceId = instance.Id,
            DatabaseName = $"db_{id}",
            DatabasePassword = "pwd",
            RedisDb = 0,
            MinioAccessKey = "key",
            MinioSecretKey = "secret",
            LiveKitApiKey = "lk_key",
            LiveKitSecretKey = "lk_secret",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.InstanceInfrastructures.Add(infra);

        await db.SaveChangesAsync();
        return instance.Id;
    }
}
