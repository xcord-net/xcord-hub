using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text;
using Testcontainers.PostgreSql;
using XcordHub.Entities;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Tests.Infrastructure;

/// <summary>
/// Integration tests for billing tier assignment in CreateInstanceHandler.
/// Verifies that the user's SubscriptionTier is correctly propagated to the
/// InstanceBilling record and that per-tier instance quotas are enforced.
/// Uses a real PostgreSQL instance via Testcontainers (no Docker-in-Docker required).
/// </summary>
[Trait("Category", "Integration")]
public sealed class BillingTierInstanceTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private string _connectionString = string.Empty;

    // ID ranges reserved for this test class to avoid conflicts with other test classes.
    // User IDs: 1_254_000_000 – 1_254_000_099
    // Instance IDs: 2_254_000_000 – 2_254_000_099
    private const long UserIdBase     = 1_254_000_000L;
    private const long InstanceIdBase = 2_254_000_000L;

    // ---------------------------------------------------------------------------
    // IAsyncLifetime
    // ---------------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("xcordhub_billing_tier_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        await using var ctx = CreateDbContext();
        await ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private const string TestEncryptionKey = "billing-tier-test-encryption-key-with-256-bits-minimum-ok!!";

    private HubDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
    }

    private static HubUser MakeUser(long id, string username, BillingTier tier) => new HubUser
    {
        Id              = id,
        Username        = username,
        DisplayName     = username,
        Email           = Encoding.UTF8.GetBytes($"encrypted-{username}@test.invalid"),
        EmailHash       = Encoding.UTF8.GetBytes($"hash-{username}"),
        PasswordHash    = "hashed_password",
        IsAdmin         = false,
        IsDisabled      = false,
        SubscriptionTier = tier,
        CreatedAt       = DateTimeOffset.UtcNow,
        LastLoginAt     = DateTimeOffset.UtcNow
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
            new SnowflakeId(workerId: 254),
            currentUserService,
            NoOpProvisioningQueue(),
            BuildConfiguration());
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateInstance_FreeTierUser_AssignsFreeTierToBillingRecord()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        var user = MakeUser(UserIdBase + 1, "billing_free_user", BillingTier.Free);
        dbContext.HubUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var handler = BuildHandler(dbContext, StubCurrentUser(user.Id));
        var command = new CreateInstanceCommand("billing-free-test", "Free Tier Instance");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue("a Free-tier user should be able to create their first instance");

        await using var verifyCtx = CreateDbContext();
        var billing = await verifyCtx.InstanceBillings
            .FirstOrDefaultAsync(b => b.ManagedInstanceId == result.Value.InstanceId);

        billing.Should().NotBeNull();
        billing!.Tier.Should().Be(BillingTier.Free,
            "the billing record tier must match the user's Free subscription tier");
    }

    [Fact]
    public async Task CreateInstance_ProTierUser_AssignsProTierToBillingRecord()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        var user = MakeUser(UserIdBase + 2, "billing_pro_user", BillingTier.Pro);
        dbContext.HubUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var handler = BuildHandler(dbContext, StubCurrentUser(user.Id));
        var command = new CreateInstanceCommand("billing-pro-test", "Pro Tier Instance");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue("a Pro-tier user should be able to create an instance");

        await using var verifyCtx = CreateDbContext();
        var billing = await verifyCtx.InstanceBillings
            .FirstOrDefaultAsync(b => b.ManagedInstanceId == result.Value.InstanceId);

        billing.Should().NotBeNull();
        billing!.Tier.Should().Be(BillingTier.Pro,
            "the billing record tier must match the user's Pro subscription tier");
    }

    [Fact]
    public async Task CreateInstance_FreeTierUser_SecondInstanceIsRejectedWithForbidden()
    {
        // Arrange — create the user and pre-seed one existing instance at the DB level
        await using var seedCtx = CreateDbContext();

        var user = MakeUser(UserIdBase + 3, "billing_free_quota_user", BillingTier.Free);
        seedCtx.HubUsers.Add(user);

        // Manually insert the first instance to simulate the quota being consumed
        // without going through the handler, so we avoid domain/subdomain conflicts.
        var firstInstance = new ManagedInstance
        {
            Id          = InstanceIdBase + 1,
            OwnerId     = user.Id,
            Domain      = "billing-quota-existing.xcord-dev.net",
            DisplayName = "Existing Instance",
            Status      = InstanceStatus.Running,
            SnowflakeWorkerId = 1,
            CreatedAt   = DateTimeOffset.UtcNow
        };
        seedCtx.ManagedInstances.Add(firstInstance);
        await seedCtx.SaveChangesAsync();

        // Act — attempt to create a second instance which should exceed Free quota (max = 1)
        await using var handlerCtx = CreateDbContext();
        var handler = BuildHandler(handlerCtx, StubCurrentUser(user.Id));
        var command = new CreateInstanceCommand("billing-quota-second", "Second Instance");

        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue(
            "a Free-tier user who already has 1 instance should be rejected when creating a second");
        result.Error!.Code.Should().Be("INSTANCE_LIMIT_REACHED");
        result.Error.StatusCode.Should().Be(403,
            "exceeding an instance quota is a Forbidden (403) error");
    }

    [Fact]
    public async Task CreateInstance_ProTierUser_CanCreateMultipleInstancesUpToQuota()
    {
        // Arrange — Pro tier allows up to 10 instances; we pre-seed 9 and verify the 10th succeeds
        await using var seedCtx = CreateDbContext();

        var user = MakeUser(UserIdBase + 4, "billing_pro_quota_user", BillingTier.Pro);
        seedCtx.HubUsers.Add(user);

        // Pre-seed 9 instances (quota limit for Pro is 10)
        for (var i = 1; i <= 9; i++)
        {
            seedCtx.ManagedInstances.Add(new ManagedInstance
            {
                Id          = InstanceIdBase + 10 + i,
                OwnerId     = user.Id,
                Domain      = $"billing-pro-existing-{i}.xcord-dev.net",
                DisplayName = $"Pro Existing {i}",
                Status      = InstanceStatus.Running,
                SnowflakeWorkerId = 10 + i,
                CreatedAt   = DateTimeOffset.UtcNow
            });
        }

        await seedCtx.SaveChangesAsync();

        // Act — 10th instance should succeed
        await using var handlerCtx = CreateDbContext();
        var handler = BuildHandler(handlerCtx, StubCurrentUser(user.Id));
        var command = new CreateInstanceCommand("billing-pro-tenth", "Pro Tenth Instance");

        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue(
            "a Pro-tier user with 9 existing instances should be allowed to create a 10th (quota = 10)");

        await using var verifyCtx = CreateDbContext();
        var billing = await verifyCtx.InstanceBillings
            .FirstOrDefaultAsync(b => b.ManagedInstanceId == result.Value.InstanceId);

        billing.Should().NotBeNull();
        billing!.Tier.Should().Be(BillingTier.Pro);
    }

    [Fact]
    public async Task CreateInstance_ProTierUser_IsRejectedWhenAtMaxQuota()
    {
        // Arrange — Pro tier allows up to 10 instances; pre-seed exactly 10
        await using var seedCtx = CreateDbContext();

        var user = MakeUser(UserIdBase + 5, "billing_pro_over_quota_user", BillingTier.Pro);
        seedCtx.HubUsers.Add(user);

        for (var i = 1; i <= 10; i++)
        {
            seedCtx.ManagedInstances.Add(new ManagedInstance
            {
                Id          = InstanceIdBase + 30 + i,
                OwnerId     = user.Id,
                Domain      = $"billing-pro-full-{i}.xcord-dev.net",
                DisplayName = $"Pro Full {i}",
                Status      = InstanceStatus.Running,
                SnowflakeWorkerId = 30 + i,
                CreatedAt   = DateTimeOffset.UtcNow
            });
        }

        await seedCtx.SaveChangesAsync();

        // Act — 11th instance should be rejected
        await using var handlerCtx = CreateDbContext();
        var handler = BuildHandler(handlerCtx, StubCurrentUser(user.Id));
        var command = new CreateInstanceCommand("billing-pro-eleventh", "Pro Eleventh Instance");

        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue(
            "a Pro-tier user who already has 10 instances should be rejected when creating an 11th");
        result.Error!.Code.Should().Be("INSTANCE_LIMIT_REACHED");
        result.Error.StatusCode.Should().Be(403);
    }
}

// ---------------------------------------------------------------------------
// Inline stubs — private to this test file
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
