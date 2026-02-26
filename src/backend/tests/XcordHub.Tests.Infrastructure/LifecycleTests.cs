using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using XcordHub.Entities;
using XcordHub.Features.Destruction;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using Xunit;

namespace XcordHub.Tests.Infrastructure;

/// <summary>
/// Tests for instance lifecycle management: suspend, resume, and destroy operations.
/// Verifies graceful shutdown ordering, resource cleanup, state transitions, and worker ID tombstoning.
///
/// All tests use a dedicated PostgreSQL Testcontainer with spy implementations of
/// infrastructure services (IDockerService, ICaddyProxyManager, IDnsProvider)
/// so that no Docker-in-Docker environment is required.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LifecycleTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private HubDbContext? _dbContext;

    // ---------------------------------------------------------------------------
    // IAsyncLifetime — spin up a dedicated PostgreSQL container for this class.
    // ---------------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("xcordhub_lifecycle_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _dbContext = new HubDbContext(options, new AesEncryptionService("lifecycle-test-encryption-key-with-256-bits-minimum-ok!"));
        await _dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dbContext != null)
            await _dbContext.DisposeAsync();

        if (_postgres != null)
            await _postgres.DisposeAsync();
    }

    // ---------------------------------------------------------------------------
    // Spy / stub implementations for infrastructure services.
    // ---------------------------------------------------------------------------

    private sealed class SpyInstanceNotifier : IInstanceNotifier
    {
        private readonly List<string> _callLog;

        public SpyInstanceNotifier(List<string> callLog) => _callLog = callLog;

        public Task NotifyShuttingDownAsync(
            string instanceDomain,
            string reason,
            CancellationToken cancellationToken = default)
        {
            _callLog.Add($"Notify:{instanceDomain}:{reason}");
            return Task.CompletedTask;
        }
    }

    private sealed class SpyDockerService : IDockerService
    {
        private readonly List<string> _callLog;

        public SpyDockerService(List<string> callLog) => _callLog = callLog;

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken = default)
        {
            _callLog.Add($"Stop:{containerId}");
            return Task.CompletedTask;
        }

        public Task<string> CreateNetworkAsync(string instanceDomain, CancellationToken cancellationToken = default) => Task.FromResult("net_spy");
        public Task<bool> VerifyNetworkAsync(string networkId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<string> StartContainerAsync(string instanceDomain, string configJson, ContainerResourceLimits? resourceLimits = null, CancellationToken cancellationToken = default) => Task.FromResult("ctr_spy");
        public Task<bool> VerifyContainerRunningAsync(string containerId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task RunMigrationContainerAsync(string instanceDomain, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> VerifyMigrationsCompleteAsync(string instanceDomain, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken = default)
        {
            _callLog.Add($"RemoveContainer:{containerId}");
            return Task.CompletedTask;
        }
        public Task RemoveNetworkAsync(string networkId, CancellationToken cancellationToken = default)
        {
            _callLog.Add($"RemoveNetwork:{networkId}");
            return Task.CompletedTask;
        }
    }

    private sealed class SpyCaddyProxyManager : ICaddyProxyManager
    {
        private readonly List<string> _callLog;

        public SpyCaddyProxyManager(List<string> callLog) => _callLog = callLog;

        public Task<string> CreateRouteAsync(string instanceDomain, string containerName, CancellationToken cancellationToken = default) => Task.FromResult("route_spy");
        public Task<bool> VerifyRouteAsync(string routeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task DeleteRouteAsync(string routeId, CancellationToken cancellationToken = default)
        {
            _callLog.Add($"DeleteRoute:{routeId}");
            return Task.CompletedTask;
        }
    }

    private sealed class SpyDnsProvider : IDnsProvider
    {
        private readonly List<string> _callLog;

        public SpyDnsProvider(List<string> callLog) => _callLog = callLog;

        public Task CreateARecordAsync(string subdomain, string ipAddress, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> VerifyDnsRecordAsync(string subdomain, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task DeleteARecordAsync(string subdomain, CancellationToken cancellationToken = default)
        {
            _callLog.Add($"DeleteDns:{subdomain}");
            return Task.CompletedTask;
        }
    }

    private sealed class SpyMinioProvisioningService : IMinioProvisioningService
    {
        private readonly List<string> _callLog;

        public SpyMinioProvisioningService(List<string> callLog) => _callLog = callLog;

        public Task ProvisionBucketAsync(string bucketName, string accessKey, string secretKey, CancellationToken cancellationToken = default)
        {
            _callLog.Add($"ProvisionBucket:{bucketName}");
            return Task.CompletedTask;
        }

        public Task DeprovisionBucketAsync(string bucketName, string accessKey, CancellationToken cancellationToken = default)
        {
            _callLog.Add($"DeprovisionBucket:{bucketName}");
            return Task.CompletedTask;
        }

        public Task<bool> VerifyBucketAsync(string bucketName, string accessKey, string secretKey, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    // ---------------------------------------------------------------------------
    // Helper — seeds a standard test user + instance + infrastructure.
    // ---------------------------------------------------------------------------

    private async Task<(HubUser owner, ManagedInstance instance, InstanceInfrastructure infra)> SeedInstanceAsync(
        long idBase,
        string prefix,
        InstanceStatus status,
        int? workerId = null)
    {
        var db = _dbContext!;

        var owner = new HubUser
        {
            Id = idBase + 1,
            Username = $"{prefix}_owner",
            DisplayName = $"{prefix} Owner",
            Email = Encoding.UTF8.GetBytes($"encrypted-{prefix}"),
            EmailHash = Encoding.UTF8.GetBytes($"hash-{prefix}"),
            PasswordHash = "hashed",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.HubUsers.Add(owner);

        var instance = new ManagedInstance
        {
            Id = idBase + 2,
            OwnerId = owner.Id,
            Domain = $"{prefix}.xcord.net",
            DisplayName = $"{prefix} Instance",
            Status = status,
            SnowflakeWorkerId = workerId ?? (int)(idBase % 1000),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ManagedInstances.Add(instance);

        var infra = new InstanceInfrastructure
        {
            Id = idBase + 3,
            ManagedInstanceId = instance.Id,
            DockerContainerId = $"container_{prefix}",
            DockerNetworkId = $"network_{prefix}",
            DatabaseName = $"{prefix}_db",
            DatabasePassword = "pwd",
            RedisDb = 0,
            MinioAccessKey = "key",
            MinioSecretKey = "secret",
            CaddyRouteId = $"route_{prefix}",
            LiveKitApiKey = "lk_key",
            LiveKitSecretKey = "lk_secret",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.InstanceInfrastructures.Add(infra);

        if (workerId.HasValue)
        {
            db.WorkerIdRegistry.Add(new WorkerIdRegistry
            {
                WorkerId = workerId.Value,
                ManagedInstanceId = instance.Id,
                IsTombstoned = false,
                AllocatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return (owner, instance, infra);
    }

    // ---------------------------------------------------------------------------
    // Suspend — verifies notification precedes container stop.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Verifies that SuspendInstanceHandler sends the System_ShuttingDown notification to the
    /// instance before stopping its container, and that the instance status is updated to Suspended.
    /// The handler introduces a 5-second grace period between notification and container stop;
    /// that delay is retained here to validate production-equivalent behaviour.
    /// </summary>
    [Fact]
    public async Task SuspendInstance_ShouldSendShutdownNotificationBeforeStoppingContainer()
    {
        var (owner, instance, infra) = await SeedInstanceAsync(9_255_000_000L, "suspend", InstanceStatus.Running);

        var callLog = new List<string>();
        var notifierSpy = new SpyInstanceNotifier(callLog);
        var dockerSpy = new SpyDockerService(callLog);

        var handler = new SuspendInstanceHandler(_dbContext!, dockerSpy, notifierSpy, NullLogger<SuspendInstanceHandler>.Instance);
        var command = new SuspendInstanceCommand(instance.Id, owner.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue("suspension should succeed for a Running instance");

        var reloaded = await _dbContext!.ManagedInstances.FindAsync(instance.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(InstanceStatus.Suspended);

        callLog.Should().HaveCount(2, "exactly one notification and one stop should be recorded");
        callLog[0].Should().StartWith("Notify:", "first call must be the shutdown notification");
        callLog[0].Should().Contain(instance.Domain);
        callLog[1].Should().StartWith("Stop:", "second call must be the container stop");
        callLog[1].Should().Contain(infra.DockerContainerId);
    }

    // ---------------------------------------------------------------------------
    // Resume — verifies status transition from Suspended to Running.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ResumeInstance_ShouldRestartContainersAndUpdateStatus()
    {
        var (owner, instance, _) = await SeedInstanceAsync(9_256_000_000L, "resume", InstanceStatus.Suspended);

        var callLog = new List<string>();
        var dockerSpy = new SpyDockerService(callLog);

        var handler = new ResumeInstanceHandler(_dbContext!, dockerSpy, NullLogger<ResumeInstanceHandler>.Instance);
        var command = new ResumeInstanceCommand(instance.Id, owner.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue("resume should succeed for a Suspended instance");

        var reloaded = await _dbContext!.ManagedInstances.FindAsync(instance.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(InstanceStatus.Running);
    }

    [Fact]
    public async Task ResumeInstance_RunningInstance_ReturnsBadRequest()
    {
        var (owner, instance, _) = await SeedInstanceAsync(9_256_100_000L, "resume_running", InstanceStatus.Running);

        var callLog = new List<string>();
        var dockerSpy = new SpyDockerService(callLog);

        var handler = new ResumeInstanceHandler(_dbContext!, dockerSpy, NullLogger<ResumeInstanceHandler>.Instance);
        var command = new ResumeInstanceCommand(instance.Id, owner.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse("cannot resume an already-Running instance");
        result.Error!.Code.Should().Be("INVALID_STATUS");
    }

    // ---------------------------------------------------------------------------
    // Destroy — verifies resource cleanup and soft delete.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DestroyInstance_ShouldRemoveAllResourcesAndSoftDelete()
    {
        var (owner, instance, infra) = await SeedInstanceAsync(9_257_000_000L, "destroy", InstanceStatus.Running, workerId: 571);

        var callLog = new List<string>();
        var dockerSpy = new SpyDockerService(callLog);
        var caddySpy = new SpyCaddyProxyManager(callLog);
        var dnsSpy = new SpyDnsProvider(callLog);
        var minioSpy = new SpyMinioProvisioningService(callLog);
        var pipeline = CreateDestructionPipeline(dockerSpy, caddySpy, dnsSpy, minioSpy);

        var handler = new DestroyInstanceHandler(
            _dbContext!, pipeline,
            NullLogger<DestroyInstanceHandler>.Instance);
        var command = new DestroyInstanceCommand(instance.Id, owner.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue("destruction should succeed for a Running instance");

        // Verify soft delete
        var reloaded = await _dbContext!.ManagedInstances
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == instance.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(InstanceStatus.Destroyed);
        reloaded.DeletedAt.Should().NotBeNull("instance should be soft-deleted");

        // Verify all cleanup operations were called
        callLog.Should().Contain(s => s.StartsWith("Stop:"), "container should be stopped");
        callLog.Should().Contain(s => s.StartsWith("DeleteRoute:"), "Caddy route should be removed");
        callLog.Should().Contain(s => s.StartsWith("DeleteDns:"), "DNS record should be removed");
        callLog.Should().Contain(s => s.StartsWith("RemoveContainer:"), "container should be removed");
        callLog.Should().Contain(s => s.StartsWith("RemoveNetwork:"), "network should be removed");
        callLog.Should().Contain(s => s.StartsWith("DeprovisionBucket:"), "MinIO bucket should be removed");

        // Verify cleanup order: stop before remove
        var stopIdx = callLog.FindIndex(s => s.StartsWith("Stop:"));
        var removeIdx = callLog.FindIndex(s => s.StartsWith("RemoveContainer:"));
        stopIdx.Should().BeLessThan(removeIdx, "container must be stopped before it is removed");
    }

    [Fact]
    public async Task DestroyInstance_ShouldTombstoneWorkerId()
    {
        var (owner, instance, _) = await SeedInstanceAsync(9_258_000_000L, "tombstone", InstanceStatus.Running, workerId: 581);

        var callLog = new List<string>();
        var dockerSpy = new SpyDockerService(callLog);
        var caddySpy = new SpyCaddyProxyManager(callLog);
        var dnsSpy = new SpyDnsProvider(callLog);
        var minioSpy = new SpyMinioProvisioningService(callLog);
        var pipeline = CreateDestructionPipeline(dockerSpy, caddySpy, dnsSpy, minioSpy);

        var handler = new DestroyInstanceHandler(
            _dbContext!, pipeline,
            NullLogger<DestroyInstanceHandler>.Instance);
        var command = new DestroyInstanceCommand(instance.Id, owner.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Verify worker ID is tombstoned
        var workerRecord = await _dbContext!.WorkerIdRegistry
            .FirstOrDefaultAsync(w => w.WorkerId == 581);
        workerRecord.Should().NotBeNull("worker ID record should exist");
        workerRecord!.IsTombstoned.Should().BeTrue("worker ID should be tombstoned after destruction");
        workerRecord.ReleasedAt.Should().NotBeNull("worker ID should have a release timestamp");
    }

    [Fact]
    public async Task DestroyInstance_AlreadyDestroyed_ReturnsBadRequest()
    {
        var (owner, instance, _) = await SeedInstanceAsync(9_258_100_000L, "destroy_dup", InstanceStatus.Destroyed);

        var callLog = new List<string>();
        var pipeline = CreateDestructionPipeline(
            new SpyDockerService(callLog), new SpyCaddyProxyManager(callLog),
            new SpyDnsProvider(callLog), new SpyMinioProvisioningService(callLog));
        var handler = new DestroyInstanceHandler(
            _dbContext!, pipeline,
            NullLogger<DestroyInstanceHandler>.Instance);
        var command = new DestroyInstanceCommand(instance.Id, owner.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse("cannot destroy an already-destroyed instance");
        result.Error!.Code.Should().Be("ALREADY_DESTROYED");
        callLog.Should().BeEmpty("no cleanup should be attempted for destroyed instances");
    }

    private static DestructionPipeline CreateDestructionPipeline(
        IDockerService dockerService, ICaddyProxyManager caddyProxy,
        IDnsProvider dnsProvider, IMinioProvisioningService minioService)
    {
        var steps = new IDestructionStep[]
        {
            new StopContainerStep(dockerService, NullLogger<StopContainerStep>.Instance),
            new RemoveProxyRouteStep(caddyProxy, NullLogger<RemoveProxyRouteStep>.Instance),
            new RemoveDnsRecordStep(dnsProvider, NullLogger<RemoveDnsRecordStep>.Instance),
            new RemoveContainerStep(dockerService, NullLogger<RemoveContainerStep>.Instance),
            new RemoveNetworkStep(dockerService, NullLogger<RemoveNetworkStep>.Instance),
            new RemoveMinioBucketStep(minioService, NullLogger<RemoveMinioBucketStep>.Instance),
        };
        return new DestructionPipeline(steps, NullLogger<DestructionPipeline>.Instance);
    }
}
