using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Text;
using XcordHub.Entities;
using XcordHub.Features.Auth;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;
using XcordHub.Shared.Extensions;
using XcordHub.Tests.Infrastructure.Fixtures;

namespace XcordHub.Tests.Infrastructure;

/// <summary>
/// Integration tests for billing tier assignment in CreateInstanceHandler.
/// Verifies that the InstanceTier and MediaEnabled values passed to CreateInstanceCommand
/// are correctly propagated to the InstanceBilling record.
/// Uses a real PostgreSQL instance via Testcontainers (no Docker-in-Docker required).
/// </summary>
[Collection("SharedPostgres")]
[Trait("Category", "Integration")]
public sealed class BillingTierInstanceTests
{
    private readonly string _connectionString;

    // ID ranges reserved for this test class to avoid conflicts with other test classes.
    // User IDs: 1_254_000_000 – 1_254_000_099
    // Instance IDs: 2_254_000_000 – 2_254_000_099
    private const long UserIdBase     = 1_254_000_000L;
    private const long InstanceIdBase = 2_254_000_000L;

    private const string TestEncryptionKey = "billing-tier-test-encryption-key-with-256-bits-minimum-ok!!";
    private static int _dbCounter;

    public BillingTierInstanceTests(SharedPostgresFixture fixture)
    {
        var dbName = $"xcordhub_billing_tier_{Interlocked.Increment(ref _dbCounter)}";
        _connectionString = fixture.CreateDatabaseAsync(dbName, TestEncryptionKey).GetAwaiter().GetResult();
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private HubDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
    }

    private static HubUser MakeUser(long id, string username) => new HubUser
    {
        Id           = id,
        Username     = username,
        DisplayName  = username,
        Email        = Encoding.UTF8.GetBytes($"encrypted-{username}@test.invalid"),
        EmailHash    = Encoding.UTF8.GetBytes($"hash-{username}"),
        PasswordHash = "hashed_password",
        IsAdmin      = false,
        IsDisabled   = false,
        CreatedAt    = DateTimeOffset.UtcNow,
        LastLoginAt  = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Returns an ICurrentUserService stub that always reports the given userId.
    /// </summary>
    private static ICurrentUserService StubCurrentUser(long userId)
        => new FixedCurrentUserService(userId);

    /// <summary>
    /// Returns an IProvisioningQueue stub that discards enqueue calls.
    /// Billing tier tests do not exercise the provisioning pipeline.
    /// </summary>
    private static IProvisioningQueue NoOpProvisioningQueue()
        => new NoOpProvisioningQueueService();

    /// <summary>
    /// Builds a minimal IConfiguration with Hub:BaseDomain set to "xcord-dev.net".
    /// </summary>
    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hub:BaseDomain"] = "xcord-dev.net"
            })
            .Build();

    /// <summary>
    /// Creates a fresh CreateInstanceHandler wired to the given dbContext.
    /// </summary>
    private static CreateInstanceHandler BuildHandler(
        HubDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        return new CreateInstanceHandler(
            dbContext,
            new SnowflakeIdGenerator(254),
            currentUserService,
            NoOpProvisioningQueue(),
            BuildConfiguration(),
            new NoOpCaptchaService(),
            Options.Create(new AuthOptions()));
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateInstance_FreeTier_AssignsCorrectTierToBillingRecord()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        var user = MakeUser(UserIdBase + 1, "billing_free_user");
        dbContext.HubUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var handler = BuildHandler(dbContext, StubCurrentUser(user.Id));
        var command = new CreateInstanceCommand(
            "billing-free-test",
            "Free Instance",
            InstanceTier.Free,
            MediaEnabled: false);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue(
            "creating an instance with the Free tier (no media) should succeed");

        await using var verifyCtx = CreateDbContext();
        var instanceId = long.Parse(result.Value.InstanceId);
        var billing = await verifyCtx.InstanceBillings
            .FirstOrDefaultAsync(b => b.ManagedInstanceId == instanceId);

        billing.Should().NotBeNull();
        billing!.Tier.Should().Be(InstanceTier.Free,
            "the billing record must reflect the Free tier passed to the command");
        billing.MediaEnabled.Should().BeFalse(
            "the billing record must reflect mediaEnabled=false passed to the command");
    }

    [Fact]
    public void Validate_PaidTier_RejectsWithPaidTierUnavailable()
    {
        using var dbContext = CreateDbContext();
        var handler = BuildHandler(dbContext, StubCurrentUser(UserIdBase + 2));

        var command = new CreateInstanceCommand(
            "paid-tier-test",
            "Paid Instance",
            InstanceTier.Basic,
            MediaEnabled: false);

        var error = handler.Validate(command);

        error.Should().NotBeNull();
        error!.Code.Should().Be("PAID_TIER_UNAVAILABLE");
    }

    [Fact]
    public void Validate_MediaEnabled_RejectsWithMediaUnavailable()
    {
        using var dbContext = CreateDbContext();
        var handler = BuildHandler(dbContext, StubCurrentUser(UserIdBase + 3));

        var command = new CreateInstanceCommand(
            "media-test",
            "Media Instance",
            InstanceTier.Free,
            MediaEnabled: true);

        var error = handler.Validate(command);

        error.Should().NotBeNull();
        error!.Code.Should().Be("MEDIA_UNAVAILABLE");
    }

    [Fact]
    public async Task CreateInstance_SecondFreeInstance_RejectsWithFreeInstanceLimit()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        var user = MakeUser(UserIdBase + 4, "billing_limit_user");
        dbContext.HubUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var handler = BuildHandler(dbContext, StubCurrentUser(user.Id));

        // Create first instance
        var first = new CreateInstanceCommand(
            "first-free-test",
            "First Instance",
            InstanceTier.Free,
            MediaEnabled: false);
        var firstResult = await handler.Handle(first, CancellationToken.None);
        firstResult.IsSuccess.Should().BeTrue("first free instance should succeed");

        // Attempt second instance
        var second = new CreateInstanceCommand(
            "second-free-test",
            "Second Instance",
            InstanceTier.Free,
            MediaEnabled: false);
        var secondResult = await handler.Handle(second, CancellationToken.None);

        // Assert
        secondResult.IsSuccess.Should().BeFalse(
            "second free instance should be rejected");
        secondResult.Error!.Code.Should().Be("FREE_INSTANCE_LIMIT");
    }

    [Fact]
    public async Task CreateInstance_AfterSoftDelete_AllowsNewFreeInstance()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        var user = MakeUser(UserIdBase + 5, "billing_softdelete_user");
        dbContext.HubUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var handler = BuildHandler(dbContext, StubCurrentUser(user.Id));

        // Create and soft-delete first instance
        var first = new CreateInstanceCommand(
            "delete-free-test",
            "Deletable Instance",
            InstanceTier.Free,
            MediaEnabled: false);
        var firstResult = await handler.Handle(first, CancellationToken.None);
        firstResult.IsSuccess.Should().BeTrue();

        var instanceId = long.Parse(firstResult.Value.InstanceId);
        var instance = await dbContext.ManagedInstances.FindAsync(instanceId);
        instance!.SoftDelete();
        await dbContext.SaveChangesAsync();

        // Create second instance - should succeed because first is soft-deleted
        var second = new CreateInstanceCommand(
            "new-free-test",
            "New Instance",
            InstanceTier.Free,
            MediaEnabled: false);
        var secondResult = await handler.Handle(second, CancellationToken.None);

        // Assert
        secondResult.IsSuccess.Should().BeTrue(
            "user should be able to create a new free instance after soft-deleting their old one");
    }
}

// ---------------------------------------------------------------------------
// Inline stubs - private to this test file
// ---------------------------------------------------------------------------

file sealed class FixedCurrentUserService : ICurrentUserService
{
    private readonly long _userId;

    public FixedCurrentUserService(long userId) => _userId = userId;

    public Result<long> GetCurrentUserId() => Result<long>.Success(_userId);
}

file sealed class NoOpProvisioningQueueService : IProvisioningQueue
{
    public Task EnqueueAsync(long instanceId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<long?> DequeueAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<long?>(null);

    public Task<List<long>> GetPendingInstancesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new List<long>());
}
