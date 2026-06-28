using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Features.Admin;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Infrastructure.Fixtures;
using Xunit;

namespace XcordHub.Tests.Infrastructure.Admin;

/// <summary>
/// Integration tests for AdminGetInstanceHandler. Verifies the handler returns
/// the full instance projection (owner username, billing tier, config JSON,
/// health, infrastructure) and surfaces NOT_FOUND for unknown IDs.
///
/// Owner IDs: 1_310_000_000 – 1_310_000_099
/// Instance IDs: 2_310_000_000 – 2_310_000_099
/// </summary>
[Collection("SharedPostgres")]
[Trait("Category", "Integration")]
public sealed class AdminGetInstanceTests : IAsyncLifetime
{
    private const string TestEncryptionKey = "admin-get-instance-tests-encryption-key-256-bits-required!!";
    private const long UserIdBase = 1_310_000_000L;
    private const long InstanceIdBase = 2_310_000_000L;

    private readonly SharedPostgresFixture _fixture;
    private string _connectionString = string.Empty;

    public AdminGetInstanceTests(SharedPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture
            .CreateDatabaseAsync("xcordhub_admin_get_instance_test", TestEncryptionKey);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HubDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
    }

    private async Task<(HubUser user, ManagedInstance instance)> SeedAsync(
        HubDbContext db,
        long userId,
        long instanceId,
        string usernameSuffix,
        string subdomain,
        InstanceTier tier = InstanceTier.Free,
        bool mediaEnabled = false)
    {
        var enc = new AesEncryptionService(TestEncryptionKey);
        var user = new HubUser
        {
            Id = userId,
            Username = $"agi_{usernameSuffix}"[..Math.Min(32, $"agi_{usernameSuffix}".Length)],
            DisplayName = $"AGI {usernameSuffix}",
            Email = enc.Encrypt($"agi_{usernameSuffix}@test.invalid"),
            EmailHash = enc.ComputeHmac($"agi_{usernameSuffix}@test.invalid"),
            PasswordHash = "hashed",
            IsAdmin = false,
            IsDisabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = DateTimeOffset.UtcNow
        };
        db.HubUsers.Add(user);

        var instance = new ManagedInstance
        {
            Id = instanceId,
            OwnerId = user.Id,
            Domain = $"{subdomain}.xcord-dev.net",
            DisplayName = $"AGI Instance {usernameSuffix}",
            Status = InstanceStatus.Running,
            SnowflakeWorkerId = (int)(instanceId - InstanceIdBase + 310),
            CreatedAt = DateTimeOffset.UtcNow,
            Billing = new InstanceBilling
            {
                Tier = tier,
                MediaEnabled = mediaEnabled,
                BillingStatus = BillingStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            Config = new InstanceConfig
            {
                ResourceLimitsJson = "{\"maxUsers\":10,\"maxChannels\":5}",
                FeatureFlagsJson = "{\"canUseVoiceChannels\":false,\"canUseVideoChannels\":false}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
        db.ManagedInstances.Add(instance);
        await db.SaveChangesAsync();
        return (user, instance);
    }

    [Fact]
    public async Task GetInstance_ReturnsFullProjection_WithOwnerAndBilling()
    {
        await using var db = CreateDbContext();
        var (user, instance) = await SeedAsync(
            db, UserIdBase + 1, InstanceIdBase + 1, "happy", "agi-happy");

        var handler = new AdminGetInstanceHandler(db);
        var result = await handler.Handle(
            new AdminGetInstanceQuery(instance.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("the instance exists and admin queries do not require ownership");
        result.Value.Id.Should().Be(instance.Id.ToString(),
            "the handler must serialize the snowflake ID as a string");
        result.Value.Subdomain.Should().Be("agi-happy",
            "the subdomain is the first label of the Domain field");
        result.Value.Domain.Should().Be("agi-happy.xcord-dev.net");
        result.Value.DisplayName.Should().Be(instance.DisplayName);
        result.Value.Status.Should().Be("Running");
        result.Value.Tier.Should().Be("Free");
        result.Value.MediaEnabled.Should().BeFalse();
        result.Value.OwnerId.Should().Be(user.Id.ToString());
        result.Value.OwnerUsername.Should().Be(user.Username);
        result.Value.ResourceLimits.Should().NotBeNull("ResourceLimitsJson should be deserialized");
        result.Value.FeatureFlags.Should().NotBeNull("FeatureFlagsJson should be deserialized");
    }

    [Fact]
    public async Task GetInstance_WithPaidTier_ReturnsTierAndMediaEnabled()
    {
        await using var db = CreateDbContext();
        var (_, instance) = await SeedAsync(
            db, UserIdBase + 2, InstanceIdBase + 2, "paid", "agi-paid",
            tier: InstanceTier.Basic, mediaEnabled: true);

        var handler = new AdminGetInstanceHandler(db);
        var result = await handler.Handle(
            new AdminGetInstanceQuery(instance.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Tier.Should().Be("Basic",
            "enums must serialize as their string name in the projection");
        result.Value.MediaEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetInstance_UnknownId_ReturnsNotFound()
    {
        await using var db = CreateDbContext();

        var handler = new AdminGetInstanceHandler(db);
        var result = await handler.Handle(
            new AdminGetInstanceQuery(999_888_777_666_555L), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("NOT_FOUND",
            "an unknown instance ID must surface NOT_FOUND, not throw");
    }

    [Fact]
    public async Task GetInstance_SoftDeleted_FilteredByDefaultQueryFilter()
    {
        await using var db = CreateDbContext();
        var (_, instance) = await SeedAsync(
            db, UserIdBase + 3, InstanceIdBase + 3, "soft", "agi-soft");
        instance.DeletedAt = DateTimeOffset.UtcNow;
        instance.Status = InstanceStatus.Destroyed;
        await db.SaveChangesAsync();

        await using var verifyDb = CreateDbContext();
        var handler = new AdminGetInstanceHandler(verifyDb);
        var result = await handler.Handle(
            new AdminGetInstanceQuery(instance.Id), CancellationToken.None);

        // ManagedInstanceConfiguration applies a global query filter for DeletedAt == null,
        // so soft-deleted instances are invisible to the standard FirstOrDefaultAsync call.
        result.IsFailure.Should().BeTrue(
            "soft-deleted instances must be filtered out by the global query filter");
        result.Error!.Code.Should().Be("NOT_FOUND");
    }
}
