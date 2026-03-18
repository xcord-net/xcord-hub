using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using XcordHub.Entities;
using XcordHub.Features.Auth;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Infrastructure.Fixtures;

namespace XcordHub.Tests.Infrastructure;

/// <summary>
/// Integration tests for InstanceCreationService.
/// Verifies that the service correctly creates a ManagedInstance, InstanceBilling, and
/// InstanceConfig in the DbContext (without calling SaveChanges itself), enforces
/// domain uniqueness, and applies the free-instance-per-user beta limit.
/// </summary>
[Collection("SharedPostgres")]
[Trait("Category", "Integration")]
public sealed class InstanceCreationServiceTests
{
    private readonly string _connectionString;

    // ID ranges reserved for this test class to avoid conflicts with other test classes.
    // User IDs: 1_248_000_000 – 1_248_000_099
    private const long UserIdBase = 1_248_000_000L;

    private const string TestEncryptionKey = "inst-creation-svc-test-encryption-key-32chars!!";

    public InstanceCreationServiceTests(SharedPostgresFixture fixture)
    {
        _connectionString = fixture.CreateDatabaseAsync("xcordhub_inst_creation_svc", TestEncryptionKey)
            .GetAwaiter().GetResult();
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

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hub:BaseDomain"] = "xcord-dev.net"
            })
            .Build();

    private static InstanceCreationService BuildService(HubDbContext db) =>
        new InstanceCreationService(
            db,
            new NoOpCaptchaService(),
            new SnowflakeIdGenerator(248),
            BuildConfiguration(),
            Options.Create(new AuthOptions { BcryptWorkFactor = 4 }));

    private async Task<HubUser> SeedUserAsync(HubDbContext db, long userId, string username)
    {
        var enc = new AesEncryptionService(TestEncryptionKey);
        var user = new HubUser
        {
            Id = userId,
            Username = username,
            DisplayName = username,
            Email = enc.Encrypt($"{username}@test.com"),
            EmailHash = enc.ComputeHmac($"{username}@test.com"),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPass123!", 4),
            IsAdmin = false,
            IsDisabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = DateTimeOffset.UtcNow
        };
        db.HubUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Create_ValidInput_CreatesInstanceWithBillingAndConfig()
    {
        // Arrange
        await using var db = CreateDbContext();
        var user = await SeedUserAsync(db, UserIdBase + 1, "valid_create_user");
        var service = BuildService(db);

        // Act
        var result = await service.CreateAsync(
            userId: user.Id,
            subdomain: "valid-create",
            displayName: "Valid Create Instance",
            adminPassword: "AdminPass123!",
            tier: InstanceTier.Free,
            mediaEnabled: false,
            skipCaptcha: true,
            captchaId: null,
            captchaAnswer: null,
            ct: CancellationToken.None);

        // Persist - service does not call SaveChanges itself
        await db.SaveChangesAsync();

        // Assert
        result.IsSuccess.Should().BeTrue("creating a valid instance should succeed");
        var instanceId = result.Value.Id;

        await using var verifyCtx = CreateDbContext();

        var instance = await verifyCtx.ManagedInstances.FindAsync(instanceId);
        instance.Should().NotBeNull("ManagedInstance should be persisted");
        instance!.OwnerId.Should().Be(user.Id);
        instance.Domain.Should().Be("valid-create.xcord-dev.net");
        instance.DisplayName.Should().Be("Valid Create Instance");

        var billing = await verifyCtx.InstanceBillings
            .FirstOrDefaultAsync(b => b.ManagedInstanceId == instanceId);
        billing.Should().NotBeNull("InstanceBilling should be persisted");
        billing!.Tier.Should().Be(InstanceTier.Free);
        billing.MediaEnabled.Should().BeFalse();
        billing.BillingStatus.Should().Be(BillingStatus.Active);

        var config = await verifyCtx.InstanceConfigs
            .FirstOrDefaultAsync(c => c.ManagedInstanceId == instanceId);
        config.Should().NotBeNull("InstanceConfig should be persisted");
        config!.ConfigJson.Should().NotBeNullOrEmpty();
        config.ResourceLimitsJson.Should().NotBeNullOrEmpty();
        config.FeatureFlagsJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Create_SubdomainTaken_ReturnsConflict()
    {
        // Arrange
        await using var db = CreateDbContext();
        var userA = await SeedUserAsync(db, UserIdBase + 10, "subdomain_user_a");
        var userB = await SeedUserAsync(db, UserIdBase + 11, "subdomain_user_b");
        var service = BuildService(db);

        // First instance claims the subdomain
        var firstResult = await service.CreateAsync(
            userId: userA.Id,
            subdomain: "taken-subdomain",
            displayName: "First",
            adminPassword: "AdminPass123!",
            tier: InstanceTier.Free,
            mediaEnabled: false,
            skipCaptcha: true,
            captchaId: null,
            captchaAnswer: null,
            ct: CancellationToken.None);
        firstResult.IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();

        // Act - different user tries the same subdomain
        var secondResult = await service.CreateAsync(
            userId: userB.Id,
            subdomain: "taken-subdomain",
            displayName: "Second",
            adminPassword: "AdminPass123!",
            tier: InstanceTier.Free,
            mediaEnabled: false,
            skipCaptcha: true,
            captchaId: null,
            captchaAnswer: null,
            ct: CancellationToken.None);

        // Assert
        secondResult.IsSuccess.Should().BeFalse("duplicate subdomain should be rejected");
        secondResult.Error!.Code.Should().Be("SUBDOMAIN_TAKEN");
    }

    [Fact]
    public async Task Create_FreeInstanceLimit_RejectsSecondFreeInstance()
    {
        // Arrange
        await using var db = CreateDbContext();
        var user = await SeedUserAsync(db, UserIdBase + 20, "free_limit_user");
        var service = BuildService(db);

        var firstResult = await service.CreateAsync(
            userId: user.Id,
            subdomain: "free-limit-first",
            displayName: "First Free",
            adminPassword: "AdminPass123!",
            tier: InstanceTier.Free,
            mediaEnabled: false,
            skipCaptcha: true,
            captchaId: null,
            captchaAnswer: null,
            ct: CancellationToken.None);
        firstResult.IsSuccess.Should().BeTrue("first free instance should succeed");

        // Persist so EF can detect it on the second call
        await db.SaveChangesAsync();

        // Act
        var secondResult = await service.CreateAsync(
            userId: user.Id,
            subdomain: "free-limit-second",
            displayName: "Second Free",
            adminPassword: "AdminPass123!",
            tier: InstanceTier.Free,
            mediaEnabled: false,
            skipCaptcha: true,
            captchaId: null,
            captchaAnswer: null,
            ct: CancellationToken.None);

        // Assert
        secondResult.IsSuccess.Should().BeFalse("second free instance should be rejected");
        secondResult.Error!.Code.Should().Be("FREE_INSTANCE_LIMIT");
    }

    [Fact]
    public async Task Create_SkipCaptcha_DoesNotValidateCaptcha()
    {
        // Arrange - pass null captcha fields; would fail validation if captcha were checked
        await using var db = CreateDbContext();
        var user = await SeedUserAsync(db, UserIdBase + 30, "skip_captcha_user");
        var service = BuildService(db);

        // Act
        var result = await service.CreateAsync(
            userId: user.Id,
            subdomain: "skip-captcha-test",
            displayName: "Skip Captcha Instance",
            adminPassword: "AdminPass123!",
            tier: InstanceTier.Free,
            mediaEnabled: false,
            skipCaptcha: true,
            captchaId: null,
            captchaAnswer: null,
            ct: CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue("skipCaptcha=true with null captcha fields should succeed");
    }

    [Fact]
    public async Task Create_DoesNotCallSaveChanges()
    {
        // Arrange
        await using var db = CreateDbContext();
        var user = await SeedUserAsync(db, UserIdBase + 40, "no_save_user");
        var service = BuildService(db);

        // Act - call CreateAsync but do NOT call SaveChanges afterward
        var result = await service.CreateAsync(
            userId: user.Id,
            subdomain: "no-save-test",
            displayName: "No Save Instance",
            adminPassword: "AdminPass123!",
            tier: InstanceTier.Free,
            mediaEnabled: false,
            skipCaptcha: true,
            captchaId: null,
            captchaAnswer: null,
            ct: CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Assert - a fresh DbContext must not see the instance (prove no auto-save occurred)
        await using var verifyCtx = CreateDbContext();
        var instanceExists = await verifyCtx.ManagedInstances
            .AnyAsync(i => i.Domain == "no-save-test.xcord-dev.net");

        instanceExists.Should().BeFalse(
            "service must not call SaveChanges; the caller is responsible for committing");
    }

    [Fact]
    public async Task Create_DomainIsSubdomainPlusBaseDomain()
    {
        // Arrange
        await using var db = CreateDbContext();
        var user = await SeedUserAsync(db, UserIdBase + 50, "domain_format_user");
        var service = BuildService(db);

        // Act
        var result = await service.CreateAsync(
            userId: user.Id,
            subdomain: "myserver",
            displayName: "My Server",
            adminPassword: "AdminPass123!",
            tier: InstanceTier.Free,
            mediaEnabled: false,
            skipCaptcha: true,
            captchaId: null,
            captchaAnswer: null,
            ct: CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Domain.Should().Be("myserver.xcord-dev.net",
            "domain must be subdomain + '.' + Hub:BaseDomain");
    }

    [Fact]
    public async Task Create_UserNotFound_ReturnsNotFound()
    {
        // Arrange - use a userId that was never seeded
        await using var db = CreateDbContext();
        var service = BuildService(db);
        const long nonExistentUserId = UserIdBase + 99;

        // Act
        var result = await service.CreateAsync(
            userId: nonExistentUserId,
            subdomain: "ghost-user-test",
            displayName: "Ghost Instance",
            adminPassword: "AdminPass123!",
            tier: InstanceTier.Free,
            mediaEnabled: false,
            skipCaptcha: true,
            captchaId: null,
            captchaAnswer: null,
            ct: CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse("unknown userId should be rejected");
        result.Error!.Code.Should().Be("USER_NOT_FOUND");
    }
}
