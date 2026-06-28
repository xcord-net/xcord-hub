using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using XcordHub.Entities;
using XcordHub.Features.Billing;
using XcordHub.Features.Monitoring;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Infrastructure.Fixtures;
using Xunit;

namespace XcordHub.Tests.Infrastructure;

/// <summary>
/// Proves billing status is enforced, not just displayed: paid instances stuck
/// in AwaitingPayment/PastDue past the grace period get suspended, exempt and
/// free billings never do, and payment resumes only billing-suspended instances.
/// </summary>
[Collection("SharedPostgres")]
[Trait("Category", "Integration")]
public sealed class BillingEnforcerTests : IAsyncLifetime
{
    private sealed class NoopInstanceNotifier : IInstanceNotifier
    {
        public Task NotifyShuttingDownAsync(string instanceDomain, string reason, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private readonly SharedPostgresFixture _fixture;
    private readonly IEncryptionService _encryptionService;
    private readonly SnowflakeIdGenerator _snowflake;
    private DbContextOptions<HubDbContext> _options = null!;
    private BillingEnforcer _enforcer = null!;

    private const string EncryptionKey = "billing-enforcer-test-encryption-key-256-bits-minimum!!!!";

    public BillingEnforcerTests(SharedPostgresFixture fixture)
    {
        _fixture = fixture;
        _encryptionService = new AesEncryptionService(EncryptionKey);
        _snowflake = new SnowflakeIdGenerator(4);
    }

    public async Task InitializeAsync()
    {
        var connectionString = await _fixture.CreateDatabaseAsync("xcordhub_billingenforcer_test", EncryptionKey);
        _options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        _enforcer = new BillingEnforcer(scopeFactory, NullLogger<BillingEnforcer>.Instance);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HubDbContext CreateContext() => new(_options, _encryptionService);

    private BillingSuspensionService CreateSuspensionService(HubDbContext db) => new(
        db,
        new NoopDockerService(NullLogger<NoopDockerService>.Instance),
        new NoopInstanceNotifier(),
        NullLogger<BillingSuspensionService>.Instance);

    private async Task<(ManagedInstance Instance, InstanceBilling Billing)> SeedAsync(
        HubDbContext db,
        InstanceTier tier,
        BillingStatus billingStatus,
        DateTimeOffset statusChangedAt,
        InstanceStatus instanceStatus = InstanceStatus.Running,
        bool billingExempt = false,
        bool billingSuspended = false)
    {
        var owner = new HubUser
        {
            Id = _snowflake.NextId(),
            Username = $"o{Guid.NewGuid().ToString("N")[..12]}",
            DisplayName = "Owner",
            Email = _encryptionService.Encrypt($"{Guid.NewGuid():N}@example.com"),
            EmailHash = _encryptionService.ComputeHmac($"{Guid.NewGuid():N}@example.com"),
            PasswordHash = "x",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.HubUsers.Add(owner);

        var instance = new ManagedInstance
        {
            Id = _snowflake.NextId(),
            OwnerId = owner.Id,
            Domain = $"{Guid.NewGuid():N}.example.com",
            DisplayName = "Billing Test Instance",
            Status = instanceStatus,
            SnowflakeWorkerId = 0,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };
        db.ManagedInstances.Add(instance);

        var billing = new InstanceBilling
        {
            Id = _snowflake.NextId(),
            ManagedInstanceId = instance.Id,
            Tier = tier,
            MediaEnabled = false,
            BillingStatus = billingStatus,
            BillingStatusChangedAt = statusChangedAt,
            BillingExempt = billingExempt,
            BillingSuspended = billingSuspended,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };
        db.InstanceBillings.Add(billing);

        await db.SaveChangesAsync();
        return (instance, billing);
    }

    private async Task RunEnforcerAsync()
    {
        await using var db = CreateContext();
        await _enforcer.EnforceAsync(db, CreateSuspensionService(db), CancellationToken.None);
    }

    private async Task<InstanceStatus> GetStatusAsync(long instanceId)
    {
        await using var db = CreateContext();
        return await db.ManagedInstances.AsNoTracking()
            .Where(i => i.Id == instanceId)
            .Select(i => i.Status)
            .FirstAsync();
    }

    [Fact]
    public async Task Enforce_AwaitingPaymentPastGrace_SuspendsInstanceAndMarksBillingSuspended()
    {
        long instanceId;
        await using (var db = CreateContext())
        {
            var (instance, _) = await SeedAsync(db, InstanceTier.Pro, BillingStatus.AwaitingPayment,
                DateTimeOffset.UtcNow - BillingEnforcer.GracePeriod - TimeSpan.FromDays(1));
            instanceId = instance.Id;
        }

        await RunEnforcerAsync();

        (await GetStatusAsync(instanceId)).Should().Be(InstanceStatus.Suspended,
            "a paid instance with no subscription past the grace period must not keep running for free");

        await using var assertDb = CreateContext();
        var billing = await assertDb.InstanceBillings.AsNoTracking().FirstAsync(b => b.ManagedInstanceId == instanceId);
        billing.BillingSuspended.Should().BeTrue("the suspension must be marked as billing-driven so payment can resume it");
    }

    [Fact]
    public async Task Enforce_PastDueWithinGrace_LeavesInstanceRunning()
    {
        long instanceId;
        await using (var db = CreateContext())
        {
            var (instance, _) = await SeedAsync(db, InstanceTier.Pro, BillingStatus.PastDue,
                DateTimeOffset.UtcNow.AddDays(-1));
            instanceId = instance.Id;
        }

        await RunEnforcerAsync();

        (await GetStatusAsync(instanceId)).Should().Be(InstanceStatus.Running,
            "the grace period exists so a failed card retry does not immediately take an instance down");
    }

    [Fact]
    public async Task Enforce_ExemptBilling_IsNeverSuspended()
    {
        long instanceId;
        await using (var db = CreateContext())
        {
            var (instance, _) = await SeedAsync(db, InstanceTier.Pro, BillingStatus.AwaitingPayment,
                DateTimeOffset.UtcNow.AddDays(-30), billingExempt: true);
            instanceId = instance.Id;
        }

        await RunEnforcerAsync();

        (await GetStatusAsync(instanceId)).Should().Be(InstanceStatus.Running,
            "BillingExempt is the deliberate escape hatch for internal/test instances");
    }

    [Fact]
    public async Task Enforce_FreeTier_IsNeverSuspended()
    {
        long instanceId;
        await using (var db = CreateContext())
        {
            var (instance, _) = await SeedAsync(db, InstanceTier.Free, BillingStatus.AwaitingPayment,
                DateTimeOffset.UtcNow.AddDays(-30));
            instanceId = instance.Id;
        }

        await RunEnforcerAsync();

        (await GetStatusAsync(instanceId)).Should().Be(InstanceStatus.Running,
            "nothing is owed on a zero-priced tier regardless of billing status");
    }

    [Fact]
    public async Task ResumeAfterPayment_BillingSuspendedInstance_ResumesAndClearsFlag()
    {
        long instanceId;
        await using (var db = CreateContext())
        {
            var (instance, _) = await SeedAsync(db, InstanceTier.Pro, BillingStatus.Active,
                DateTimeOffset.UtcNow, instanceStatus: InstanceStatus.Suspended, billingSuspended: true);
            instanceId = instance.Id;
        }

        await using (var db = CreateContext())
        {
            var billing = await db.InstanceBillings.FirstAsync(b => b.ManagedInstanceId == instanceId);
            await CreateSuspensionService(db).ResumeAfterPaymentAsync(billing, CancellationToken.None);
        }

        (await GetStatusAsync(instanceId)).Should().Be(InstanceStatus.Running,
            "paying must bring a billing-suspended instance back");

        await using var assertDb = CreateContext();
        var reloaded = await assertDb.InstanceBillings.AsNoTracking().FirstAsync(b => b.ManagedInstanceId == instanceId);
        reloaded.BillingSuspended.Should().BeFalse();
    }

    [Fact]
    public async Task ResumeAfterPayment_ManuallySuspendedInstance_StaysSuspended()
    {
        long instanceId;
        await using (var db = CreateContext())
        {
            var (instance, _) = await SeedAsync(db, InstanceTier.Pro, BillingStatus.Active,
                DateTimeOffset.UtcNow, instanceStatus: InstanceStatus.Suspended, billingSuspended: false);
            instanceId = instance.Id;
        }

        await using (var db = CreateContext())
        {
            var billing = await db.InstanceBillings.FirstAsync(b => b.ManagedInstanceId == instanceId);
            await CreateSuspensionService(db).ResumeAfterPaymentAsync(billing, CancellationToken.None);
        }

        (await GetStatusAsync(instanceId)).Should().Be(InstanceStatus.Suspended,
            "a payment webhook must never resume an instance an admin suspended for other reasons");
    }
}
