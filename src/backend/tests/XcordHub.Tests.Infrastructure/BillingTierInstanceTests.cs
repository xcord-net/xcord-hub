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
/// Verifies that the FeatureTier and UserCountTier passed to CreateInstanceCommand
/// are correctly propagated to the InstanceBilling record.
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
            new SnowflakeId(workerId: 254),
            currentUserService,
            NoOpProvisioningQueue(),
            BuildConfiguration());
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateInstance_ChatPlusTier10_AssignsCorrectTiersToBillingRecord()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        var user = MakeUser(UserIdBase + 1, "billing_chat_tier10_user");
        dbContext.HubUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var handler = BuildHandler(dbContext, StubCurrentUser(user.Id));
        var command = new CreateInstanceCommand(
            "billing-chat-tier10-test",
            "Chat Tier10 Instance",
            FeatureTier.Chat,
            UserCountTier.Tier10);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue(
            "creating an instance with Chat+Tier10 (free plan) should succeed");

        await using var verifyCtx = CreateDbContext();
        var instanceId = long.Parse(result.Value.InstanceId);
        var billing = await verifyCtx.InstanceBillings
            .FirstOrDefaultAsync(b => b.ManagedInstanceId == instanceId);

        billing.Should().NotBeNull();
        billing!.FeatureTier.Should().Be(FeatureTier.Chat,
            "the billing record must reflect the Chat feature tier passed to the command");
        billing.UserCountTier.Should().Be(UserCountTier.Tier10,
            "the billing record must reflect the Tier10 user count tier passed to the command");
    }

    [Fact]
    public async Task CreateInstance_AudioPlusTier50_AssignsCorrectTiersToBillingRecord()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        var user = MakeUser(UserIdBase + 2, "billing_audio_tier50_user");
        dbContext.HubUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var handler = BuildHandler(dbContext, StubCurrentUser(user.Id));
        var command = new CreateInstanceCommand(
            "billing-audio-tier50-test",
            "Audio Tier50 Instance",
            FeatureTier.Audio,
            UserCountTier.Tier50);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue(
            "creating an instance with Audio+Tier50 (paid plan) should succeed");

        await using var verifyCtx = CreateDbContext();
        var instanceId = long.Parse(result.Value.InstanceId);
        var billing = await verifyCtx.InstanceBillings
            .FirstOrDefaultAsync(b => b.ManagedInstanceId == instanceId);

        billing.Should().NotBeNull();
        billing!.FeatureTier.Should().Be(FeatureTier.Audio,
            "the billing record must reflect the Audio feature tier passed to the command");
        billing.UserCountTier.Should().Be(UserCountTier.Tier50,
            "the billing record must reflect the Tier50 user count tier passed to the command");
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
