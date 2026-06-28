using FluentAssertions;
using XcordHub.Entities;
using XcordHub.Features.Admin;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Infrastructure.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace XcordHub.Tests.Infrastructure.Admin;

/// <summary>
/// Integration tests for AdminListInstancesHandler. Verifies pagination clamps,
/// status filtering, and that seeded instances are surfaced with owner + billing
/// projection fields populated.
///
/// Owner IDs: 1_311_000_000 – 1_311_000_099
/// Instance IDs: 2_311_000_000 – 2_311_000_099
/// </summary>
[Collection("SharedPostgres")]
[Trait("Category", "Integration")]
public sealed class AdminListInstancesTests : IAsyncLifetime
{
    private const string TestEncryptionKey = "admin-list-instances-tests-encryption-key-256-bits-req!!";
    private const long UserIdBase = 1_311_000_000L;
    private const long InstanceIdBase = 2_311_000_000L;

    private readonly SharedPostgresFixture _fixture;
    private string _connectionString = string.Empty;

    public AdminListInstancesTests(SharedPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture
            .CreateDatabaseAsync("xcordhub_admin_list_instances_test", TestEncryptionKey);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HubDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
    }

    private async Task<ManagedInstance> SeedInstanceAsync(
        HubDbContext db,
        long userId,
        long instanceId,
        string usernameSuffix,
        string subdomain,
        InstanceStatus status = InstanceStatus.Running,
        InstanceTier tier = InstanceTier.Free)
    {
        var enc = new AesEncryptionService(TestEncryptionKey);
        var user = new HubUser
        {
            Id = userId,
            Username = $"ali_{usernameSuffix}"[..Math.Min(32, $"ali_{usernameSuffix}".Length)],
            DisplayName = $"ALI {usernameSuffix}",
            Email = enc.Encrypt($"ali_{usernameSuffix}@test.invalid"),
            EmailHash = enc.ComputeHmac($"ali_{usernameSuffix}@test.invalid"),
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
            DisplayName = $"ALI Instance {usernameSuffix}",
            Status = status,
            SnowflakeWorkerId = (int)(instanceId - InstanceIdBase + 311),
            CreatedAt = DateTimeOffset.UtcNow,
            Billing = new InstanceBilling
            {
                Tier = tier,
                MediaEnabled = false,
                BillingStatus = BillingStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
            }
        };
        db.ManagedInstances.Add(instance);
        await db.SaveChangesAsync();
        return instance;
    }

    [Fact]
    public async Task ListInstances_DefaultPaging_ReturnsSeededInstanceWithOwnerUsername()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedInstanceAsync(
            db, UserIdBase + 1, InstanceIdBase + 1, "happy", "ali-happy");

        var handler = new AdminListInstancesHandler(db);
        var result = await handler.Handle(
            new AdminListInstancesQuery(Page: 1, PageSize: 100), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(100);
        result.Value.Total.Should().BeGreaterThanOrEqualTo(1,
            "Total reflects the unpaged count after filtering");

        var dto = result.Value.Instances.FirstOrDefault(i => i.Id == seeded.Id.ToString());
        dto.Should().NotBeNull("the seeded instance must appear in the list");
        dto!.Subdomain.Should().Be("ali-happy");
        dto.Status.Should().Be("Running");
        dto.Tier.Should().Be("Free");
        dto.OwnerUsername.Should().StartWith("ali_happy");
    }

    [Fact]
    public async Task ListInstances_StatusFilter_RestrictsResults()
    {
        await using var db = CreateDbContext();
        // Seed two instances - one Running, one Suspended.
        var running = await SeedInstanceAsync(
            db, UserIdBase + 2, InstanceIdBase + 2, "running", "ali-running",
            status: InstanceStatus.Running);
        var suspended = await SeedInstanceAsync(
            db, UserIdBase + 3, InstanceIdBase + 3, "susp", "ali-susp",
            status: InstanceStatus.Suspended);

        var handler = new AdminListInstancesHandler(db);
        var result = await handler.Handle(
            new AdminListInstancesQuery(Page: 1, PageSize: 100, Status: "Suspended"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Instances.Should().Contain(i => i.Id == suspended.Id.ToString(),
            "Suspended instances must match the case-insensitive status filter");
        result.Value.Instances.Should().NotContain(i => i.Id == running.Id.ToString(),
            "Running instances must be filtered out");
    }

    [Fact]
    public async Task ListInstances_PageSizeClamp_ClampsAboveMaximumTo100()
    {
        await using var db = CreateDbContext();

        var handler = new AdminListInstancesHandler(db);
        var result = await handler.Handle(
            new AdminListInstancesQuery(Page: 1, PageSize: 99999), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PageSize.Should().Be(100,
            "the handler must clamp pageSize to [1,100] to bound DB load");
    }

    [Fact]
    public async Task ListInstances_PageBelowOne_ClampedToOne()
    {
        await using var db = CreateDbContext();

        var handler = new AdminListInstancesHandler(db);
        var result = await handler.Handle(
            new AdminListInstancesQuery(Page: -5, PageSize: 25), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Page.Should().Be(1,
            "negative or zero page values must be clamped to page 1");
    }
}
