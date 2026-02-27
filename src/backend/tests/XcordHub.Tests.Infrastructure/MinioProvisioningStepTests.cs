using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using XcordHub.Entities;
using XcordHub.Features.Provisioning;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;
using Xunit;

namespace XcordHub.Tests.Infrastructure;

/// <summary>
/// Tests for <see cref="ProvisionMinioStep"/> — the provisioning pipeline step that
/// creates a per-instance MinIO bucket and IAM user.
///
/// These tests use spy implementations of <see cref="IMinioProvisioningService"/>
/// and a real PostgreSQL Testcontainer, matching the pattern used by LifecycleTests.
/// No actual MinIO server is required.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MinioProvisioningStepTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private HubDbContext? _dbContext;

    // ID ranges reserved for this test class — must not overlap with other test classes.
    // User IDs: 9_278_000_000 – 9_278_000_099
    // Instance IDs: 9_279_000_000 – 9_279_000_099
    private const long UserIdBase     = 9_278_000_000L;
    private const long InstanceIdBase = 9_279_000_000L;

    private const string TestEncryptionKey =
        "minio-step-test-encryption-key-with-256-bits-minimum-ok!!";

    // ──────────── IAsyncLifetime ────────────

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("xcordhub_minio_step_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _dbContext = new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
        await _dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dbContext != null)
            await _dbContext.DisposeAsync();

        if (_postgres != null)
            await _postgres.DisposeAsync();
    }

    // ──────────── Spy implementations ────────────

    /// <summary>
    /// Records every call made to <see cref="IMinioProvisioningService"/> in a list.
    /// </summary>
    private sealed class RecordingMinioService : IMinioProvisioningService
    {
        public List<string> Calls { get; } = new();

        /// <summary>
        /// When true, <see cref="VerifyBucketAsync"/> returns false (simulating Console API failure).
        /// </summary>
        public bool SimulateVerifyFailure { get; set; }

        public Task ProvisionBucketAsync(
            string bucketName, string accessKey, string secretKey,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"Provision:{bucketName}:{accessKey}");
            return Task.CompletedTask;
        }

        public Task DeprovisionBucketAsync(
            string bucketName, string accessKey,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"Deprovision:{bucketName}:{accessKey}");
            return Task.CompletedTask;
        }

        public Task<bool> VerifyBucketAsync(
            string bucketName, string accessKey, string secretKey,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"Verify:{bucketName}:{accessKey}");
            return Task.FromResult(!SimulateVerifyFailure);
        }
    }

    /// <summary>
    /// Throws an exception from <see cref="ProvisionBucketAsync"/> to simulate a MinIO outage.
    /// </summary>
    private sealed class FailingMinioService : IMinioProvisioningService
    {
        public Task ProvisionBucketAsync(
            string bucketName, string accessKey, string secretKey,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("MinIO is unavailable (simulated)");

        public Task DeprovisionBucketAsync(
            string bucketName, string accessKey,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> VerifyBucketAsync(
            string bucketName, string accessKey, string secretKey,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    // ──────────── Helpers ────────────

    private static IOptions<MinioOptions> BuildMinioOptions(
        string accessKey = "rootkey",
        string secretKey = "rootsecret",
        string endpoint = "localhost:9000")
        => Options.Create(new MinioOptions
        {
            AccessKey = accessKey,
            SecretKey = secretKey,
            Endpoint = endpoint,
            UseSsl = false
        });

    private async Task<(HubUser owner, ManagedInstance instance, InstanceInfrastructure infra)> SeedInstanceAsync(
        long idBase,
        string subdomain,
        string minioAccessKey = "instance-access-key",
        string minioSecretKey = "instance-secret-key")
    {
        var db = _dbContext!;

        var owner = new HubUser
        {
            Id = idBase,
            Username = $"owner_{subdomain}",
            DisplayName = $"Owner {subdomain}",
            Email = Encoding.UTF8.GetBytes($"encrypted-{subdomain}@test.invalid"),
            EmailHash = Encoding.UTF8.GetBytes($"hash-{subdomain}"),
            PasswordHash = "hashed",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.HubUsers.Add(owner);

        var instance = new ManagedInstance
        {
            Id = idBase + 1,
            OwnerId = owner.Id,
            Domain = $"{subdomain}.xcord.net",
            DisplayName = $"{subdomain} Instance",
            Status = InstanceStatus.Provisioning,
            SnowflakeWorkerId = idBase % 1000,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ManagedInstances.Add(instance);

        var infra = new InstanceInfrastructure
        {
            Id = idBase + 2,
            ManagedInstanceId = instance.Id,
            DockerContainerId = $"container_{subdomain}",
            DockerNetworkId = $"network_{subdomain}",
            DatabaseName = $"{subdomain}_db",
            DatabasePassword = "pwd",
            RedisDb = 0,
            MinioAccessKey = minioAccessKey,
            MinioSecretKey = minioSecretKey,
            CaddyRouteId = $"route_{subdomain}",
            LiveKitApiKey = "lk_key",
            LiveKitSecretKey = "lk_secret",
            InstanceKek = "test-kek",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.InstanceInfrastructures.Add(infra);

        await db.SaveChangesAsync();
        return (owner, instance, infra);
    }

    private ProvisionMinioStep BuildStep(IMinioProvisioningService minioService, MinioOptions? options = null)
    {
        var minioOptions = options != null
            ? Options.Create(options)
            : BuildMinioOptions();

        return new ProvisionMinioStep(
            _dbContext!,
            minioService,
            minioOptions,
            NullLogger<ProvisionMinioStep>.Instance);
    }

    // ──────────── ExecuteAsync ────────────

    [Fact]
    public async Task ExecuteAsync_SuccessfulProvisioning_CallsProvisionAndVerify()
    {
        var (_, instance, infra) = await SeedInstanceAsync(InstanceIdBase + 10, "provtest");

        var minioSpy = new RecordingMinioService();
        var step = BuildStep(minioSpy);

        var result = await step.ExecuteAsync(instance.Id);

        result.IsSuccess.Should().BeTrue("provisioning should succeed when MinIO service works");

        var expectedBucket = "xcord-provtest";
        minioSpy.Calls.Should().Contain(
            $"Provision:{expectedBucket}:{infra.MinioAccessKey}",
            "ProvisionBucketAsync should be called with the derived bucket name and instance access key");
        minioSpy.Calls.Should().Contain(
            $"Verify:{expectedBucket}:{infra.MinioAccessKey}",
            "VerifyBucketAsync should be called to confirm per-instance credentials work");
    }

    [Fact]
    public async Task ExecuteAsync_PerInstanceVerifyFails_FallsBackToRootCredentials()
    {
        // Simulate: bucket is created successfully but the Console API did not create
        // a per-instance IAM user, so verify returns false.
        var instanceAccessKey = "per-instance-key";
        var rootAccessKey = "root-key";
        var rootSecretKey = "root-secret";

        var (_, instance, _) = await SeedInstanceAsync(
            InstanceIdBase + 20, "fallback", instanceAccessKey, "per-instance-secret");

        var minioSpy = new RecordingMinioService { SimulateVerifyFailure = true };
        var rootMinioOptions = new MinioOptions
        {
            AccessKey = rootAccessKey,
            SecretKey = rootSecretKey,
            Endpoint = "localhost:9000",
            UseSsl = false
        };
        var step = BuildStep(minioSpy, rootMinioOptions);

        var result = await step.ExecuteAsync(instance.Id);

        result.IsSuccess.Should().BeTrue("step should succeed even when per-instance credentials fail");

        // Verify the infra record now holds root credentials
        await using var verifyDb = CreateVerifyDbContext();
        var reloadedInfra = await verifyDb.InstanceInfrastructures
            .FirstOrDefaultAsync(i => i.ManagedInstanceId == instance.Id);

        reloadedInfra.Should().NotBeNull();
        reloadedInfra!.MinioAccessKey.Should().Be(rootAccessKey,
            "when per-instance credentials fail, infrastructure should be updated to use root access key");
        reloadedInfra.MinioSecretKey.Should().Be(rootSecretKey,
            "when per-instance credentials fail, infrastructure should be updated to use root secret key");
    }

    [Fact]
    public async Task ExecuteAsync_ProvisioningServiceThrows_ReturnsFailure()
    {
        var (_, instance, _) = await SeedInstanceAsync(InstanceIdBase + 30, "failtest");

        var failingService = new FailingMinioService();
        var step = BuildStep(failingService);

        var result = await step.ExecuteAsync(instance.Id);

        result.IsSuccess.Should().BeFalse("step should return failure when MinIO service throws");
        result.Error!.Code.Should().Be("MINIO_PROVISION_FAILED",
            "error code should be MINIO_PROVISION_FAILED on provisioning exception");
        result.Error.Message.Should().Contain("MinIO is unavailable (simulated)");
    }

    [Fact]
    public async Task ExecuteAsync_InfrastructureNotFound_ReturnsNotFoundError()
    {
        // Use a non-existent instance ID — no infrastructure seeded
        const long nonExistentId = 999_000_000_000L;

        var minioSpy = new RecordingMinioService();
        var step = BuildStep(minioSpy);

        var result = await step.ExecuteAsync(nonExistentId);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("INFRASTRUCTURE_NOT_FOUND");

        minioSpy.Calls.Should().BeEmpty(
            "no MinIO calls should be made when the infrastructure record is missing");
    }

    [Fact]
    public async Task ExecuteAsync_BucketNameDerivedFromSubdomain()
    {
        // Verifies that the bucket name is "xcord-{first-segment-of-domain}"
        var (_, instance, _) = await SeedInstanceAsync(InstanceIdBase + 40, "myinstance");

        var minioSpy = new RecordingMinioService();
        var step = BuildStep(minioSpy);

        await step.ExecuteAsync(instance.Id);

        minioSpy.Calls.Should().Contain(c => c.StartsWith("Provision:xcord-myinstance:"),
            "bucket name should be 'xcord-{subdomain}' where subdomain is the first label of the domain");
    }

    [Fact]
    public async Task ExecuteAsync_StepName_IsProvisionMinio()
    {
        var minioSpy = new RecordingMinioService();
        var step = BuildStep(minioSpy);

        step.StepName.Should().Be("ProvisionMinio");
    }

    // ──────────── VerifyAsync ────────────

    [Fact]
    public async Task VerifyAsync_BucketExists_ReturnsSuccess()
    {
        var (_, instance, infra) = await SeedInstanceAsync(InstanceIdBase + 50, "verifyok");

        var minioSpy = new RecordingMinioService(); // SimulateVerifyFailure = false by default
        var step = BuildStep(minioSpy);

        var result = await step.VerifyAsync(instance.Id);

        result.IsSuccess.Should().BeTrue("verify should succeed when MinIO reports bucket accessible");
        minioSpy.Calls.Should().Contain(
            $"Verify:xcord-verifyok:{infra.MinioAccessKey}",
            "VerifyBucketAsync should be called during the verify step");
    }

    [Fact]
    public async Task VerifyAsync_BucketMissing_ReturnsFailure()
    {
        var (_, instance, _) = await SeedInstanceAsync(InstanceIdBase + 60, "verifyfail");

        var minioSpy = new RecordingMinioService { SimulateVerifyFailure = true };
        var step = BuildStep(minioSpy);

        var result = await step.VerifyAsync(instance.Id);

        result.IsSuccess.Should().BeFalse("verify should fail when bucket is not accessible");
        result.Error!.Code.Should().Be("MINIO_VERIFY_FAILED");
    }

    [Fact]
    public async Task VerifyAsync_InfrastructureNotFound_ReturnsNotFoundError()
    {
        const long nonExistentId = 999_000_001_000L;

        var minioSpy = new RecordingMinioService();
        var step = BuildStep(minioSpy);

        var result = await step.VerifyAsync(nonExistentId);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("INFRASTRUCTURE_NOT_FOUND");
    }

    // ──────────── Interface compliance ────────────

    [Fact]
    public void IMinioProvisioningService_HasProvisionBucketAsync()
    {
        // Verifies the interface exposes the method the provisioning pipeline depends on.
        var method = typeof(IMinioProvisioningService)
            .GetMethod("ProvisionBucketAsync",
                [typeof(string), typeof(string), typeof(string), typeof(CancellationToken)]);

        method.Should().NotBeNull(
            "IMinioProvisioningService must expose ProvisionBucketAsync(bucketName, accessKey, secretKey, ct)");
    }

    [Fact]
    public void IMinioProvisioningService_HasDeprovisionBucketAsync()
    {
        var method = typeof(IMinioProvisioningService)
            .GetMethod("DeprovisionBucketAsync",
                [typeof(string), typeof(string), typeof(CancellationToken)]);

        method.Should().NotBeNull(
            "IMinioProvisioningService must expose DeprovisionBucketAsync(bucketName, accessKey, ct)");
    }

    [Fact]
    public void IMinioProvisioningService_HasVerifyBucketAsync()
    {
        var method = typeof(IMinioProvisioningService)
            .GetMethod("VerifyBucketAsync",
                [typeof(string), typeof(string), typeof(string), typeof(CancellationToken)]);

        method.Should().NotBeNull(
            "IMinioProvisioningService must expose VerifyBucketAsync(bucketName, accessKey, secretKey, ct)");
    }

    [Fact]
    public void MinioProvisioningService_ImplementsIMinioProvisioningService()
    {
        typeof(IMinioProvisioningService).IsAssignableFrom(typeof(MinioProvisioningService))
            .Should().BeTrue(
                "MinioProvisioningService must implement IMinioProvisioningService so it can be replaced with test spies");
    }

    // ──────────── Helpers (private) ────────────

    private HubDbContext CreateVerifyDbContext()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_postgres!.GetConnectionString())
            .Options;
        return new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
    }
}
