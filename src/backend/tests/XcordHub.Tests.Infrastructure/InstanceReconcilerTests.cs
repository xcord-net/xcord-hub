using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using XcordHub.Entities;
using XcordHub.Features.Monitoring;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Infrastructure.Fixtures;
using Xunit;

namespace XcordHub.Tests.Infrastructure;

/// <summary>
/// Proves the reconciler's provisioning recovery actually recovers: stuck
/// Provisioning instances must be re-enqueued in a dequeueable state (the old
/// code reset them to Pending, which nothing dequeues, abandoning them), retry
/// attempts must be capped, and instances stranded in Pending by a failed
/// enqueue must be swept back into the queue.
/// </summary>
[Collection("SharedPostgres")]
[Trait("Category", "Integration")]
public sealed class InstanceReconcilerTests
{
    private readonly DbContextOptions<HubDbContext> _options;
    private readonly IEncryptionService _encryptionService;
    private readonly SnowflakeIdGenerator _snowflake;
    private readonly InstanceReconciler _reconciler;

    private const string EncryptionKey = "reconciler-test-encryption-key-with-256-bits-minimum-len!!";

    public InstanceReconcilerTests(SharedPostgresFixture fixture)
    {
        var connectionString = fixture.CreateDatabaseAsync("xcordhub_reconciler_test", EncryptionKey).GetAwaiter().GetResult();
        _options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        _encryptionService = new AesEncryptionService(EncryptionKey);
        _snowflake = new SnowflakeIdGenerator(3);

        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        _reconciler = new InstanceReconciler(scopeFactory, NullLogger<InstanceReconciler>.Instance);
    }

    private HubDbContext CreateContext() => new(_options, _encryptionService);

    private async Task<ManagedInstance> SeedInstanceAsync(
        HubDbContext db,
        InstanceStatus status,
        DateTimeOffset? lastAttemptAt,
        int attempts,
        DateTimeOffset? createdAt = null)
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
            DisplayName = "Test Instance",
            Status = status,
            SnowflakeWorkerId = 0,
            ProvisioningAttempts = attempts,
            LastProvisioningAttemptAt = lastAttemptAt,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow.AddHours(-1)
        };
        db.ManagedInstances.Add(instance);
        await db.SaveChangesAsync();
        return instance;
    }

    [Fact]
    public async Task DetectStuckProvisioning_StaleInstance_ReEnqueuesInDequeueableState()
    {
        await using var db = CreateContext();
        var instance = await SeedInstanceAsync(db, InstanceStatus.Provisioning,
            lastAttemptAt: DateTimeOffset.UtcNow.AddMinutes(-10), attempts: 1);

        await _reconciler.DetectStuckProvisioningAsync(db, new DatabaseProvisioningQueue(db), CancellationToken.None);

        await using var assertDb = CreateContext();
        var reloaded = await assertDb.ManagedInstances.AsNoTracking().FirstAsync(i => i.Id == instance.Id);
        reloaded.Status.Should().Be(InstanceStatus.Provisioning,
            "a re-enqueued instance must stay in the status the queue dequeues");
        reloaded.ProvisioningAttempts.Should().Be(2, "the retry must be counted");
        reloaded.LastProvisioningAttemptAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1),
            "the stuck-detection window must reset for the new attempt");

        var dequeued = await new DatabaseProvisioningQueue(assertDb).DequeueAsync();
        dequeued.Should().Be(instance.Id, "the retried instance must be visible to the provisioning worker");
    }

    [Fact]
    public async Task DetectStuckProvisioning_AttemptsExhausted_MarksInstanceFailed()
    {
        await using var db = CreateContext();
        var instance = await SeedInstanceAsync(db, InstanceStatus.Provisioning,
            lastAttemptAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            attempts: InstanceReconciler.MaxProvisioningAttempts);

        await _reconciler.DetectStuckProvisioningAsync(db, new DatabaseProvisioningQueue(db), CancellationToken.None);

        await using var assertDb = CreateContext();
        var reloaded = await assertDb.ManagedInstances.AsNoTracking().FirstAsync(i => i.Id == instance.Id);
        reloaded.Status.Should().Be(InstanceStatus.Failed,
            "endless retries would loop forever on an unprovisionable instance");
    }

    [Fact]
    public async Task DetectStuckProvisioning_RecentAttempt_LeavesInstanceAlone()
    {
        await using var db = CreateContext();
        var instance = await SeedInstanceAsync(db, InstanceStatus.Provisioning,
            lastAttemptAt: DateTimeOffset.UtcNow.AddMinutes(-1), attempts: 1,
            createdAt: DateTimeOffset.UtcNow.AddHours(-2));

        await _reconciler.DetectStuckProvisioningAsync(db, new DatabaseProvisioningQueue(db), CancellationToken.None);

        await using var assertDb = CreateContext();
        var reloaded = await assertDb.ManagedInstances.AsNoTracking().FirstAsync(i => i.Id == instance.Id);
        reloaded.ProvisioningAttempts.Should().Be(1,
            "an in-progress attempt within the timeout window is not stuck, even on an old instance");
    }

    [Fact]
    public async Task SweepOrphanedPending_StrandedInstance_GetsEnqueued()
    {
        await using var db = CreateContext();
        var instance = await SeedInstanceAsync(db, InstanceStatus.Pending,
            lastAttemptAt: null, attempts: 0,
            createdAt: DateTimeOffset.UtcNow.AddMinutes(-10));

        await _reconciler.SweepOrphanedPendingAsync(db, new DatabaseProvisioningQueue(db), CancellationToken.None);

        await using var assertDb = CreateContext();
        var reloaded = await assertDb.ManagedInstances.AsNoTracking().FirstAsync(i => i.Id == instance.Id);
        reloaded.Status.Should().Be(InstanceStatus.Provisioning,
            "an instance whose creation-time enqueue was lost must be recovered into the queue");
        reloaded.ProvisioningAttempts.Should().Be(1);
    }

    [Fact]
    public async Task SweepOrphanedPending_FreshlyCreatedInstance_IsLeftAlone()
    {
        await using var db = CreateContext();
        var instance = await SeedInstanceAsync(db, InstanceStatus.Pending,
            lastAttemptAt: null, attempts: 0,
            createdAt: DateTimeOffset.UtcNow);

        await _reconciler.SweepOrphanedPendingAsync(db, new DatabaseProvisioningQueue(db), CancellationToken.None);

        await using var assertDb = CreateContext();
        var reloaded = await assertDb.ManagedInstances.AsNoTracking().FirstAsync(i => i.Id == instance.Id);
        reloaded.Status.Should().Be(InstanceStatus.Pending,
            "creation-path enqueue is about to run; the sweep must not race it");
    }
}
