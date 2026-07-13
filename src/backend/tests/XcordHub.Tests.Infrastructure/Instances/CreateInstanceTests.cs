using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xcord.Captcha;
using XcordHub.Entities;
using XcordHub.Features.Billing;
using XcordHub.Features.Instances;
using XcordHub.Features.Provisioning;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Infrastructure.Billing;
using XcordHub.Tests.Infrastructure.Fixtures;
using Xunit;

namespace XcordHub.Tests.Infrastructure.Instances;

/// <summary>
/// Integration tests for CreateInstanceHandler. Covers the validation gates
/// (reserved subdomains, paid tier without Stripe, paid-servers-disabled
/// global flag) plus the happy path of creating a free-tier instance and
/// enqueueing it for background provisioning.
///
/// Owner IDs: 1_312_000_000 – 1_312_000_099
/// </summary>
[Collection("SharedPostgres")]
[Trait("Category", "Integration")]
public sealed class CreateInstanceTests : IAsyncLifetime
{
    private const string TestEncryptionKey = "create-instance-handler-tests-encryption-key-256-req!!";
    private const long UserIdBase = 1_312_000_000L;

    private readonly SharedPostgresFixture _fixture;
    private string _connectionString = string.Empty;

    public CreateInstanceTests(SharedPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture
            .CreateDatabaseAsync("xcordhub_create_instance_handler_test", TestEncryptionKey);
    }

    public Task DisposeAsync() => Task.CompletedTask;

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

    private async Task<HubUser> SeedUserAsync(HubDbContext db, long userId, string username)
    {
        var enc = new AesEncryptionService(TestEncryptionKey);
        var user = new HubUser
        {
            Id = userId,
            Username = username,
            DisplayName = username,
            Email = enc.Encrypt($"{username}@test.invalid"),
            EmailHash = enc.ComputeHmac($"{username}@test.invalid"),
            PasswordHash = "hashed",
            IsAdmin = false,
            IsDisabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = DateTimeOffset.UtcNow
        };
        db.HubUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private CreateInstanceHandler BuildHandler(
        HubDbContext db,
        long currentUserId,
        IOptions<StripeOptions>? stripeOptions = null)
    {
        var creationService = new InstanceCreationService(
            db,
            new NoOpCaptchaService(),
            new SnowflakeIdGenerator(312),
            BuildConfiguration(),
            Options.Create(new AuthOptions { BcryptWorkFactor = 4 }),
            stripeOptions ?? Options.Create(new StripeOptions()));

        return new CreateInstanceHandler(
            db,
            new FixedCurrentUserService(currentUserId),
            new NoOpProvisioningQueue(),
            creationService,
            new SystemConfigService(db),
            stripeOptions ?? Options.Create(new StripeOptions()));
    }

    [Fact]
    public async Task CreateInstance_FreeTier_PersistsManagedInstanceAndReturnsCredentials()
    {
        await using var db = CreateDbContext();
        var user = await SeedUserAsync(db, UserIdBase + 1, "create_happy_user");
        var handler = BuildHandler(db, user.Id);

        var command = new CreateInstanceCommand("createhappy", "Create Happy", InstanceTier.Free);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Domain.Should().Be("createhappy.xcord-dev.net",
            "the handler must compose Domain from the subdomain + Hub:BaseDomain config");
        result.Value.Status.Should().Be("Pending",
            "new instances start in Pending until the background provisioner picks them up");
        result.Value.AdminPassword.Should().NotBeNullOrWhiteSpace(
            "the handler must auto-generate an admin password when none is supplied");

        await using var verifyDb = CreateDbContext();
        var persisted = await verifyDb.ManagedInstances
            .FirstOrDefaultAsync(i => i.Id == long.Parse(result.Value.InstanceId));
        persisted.Should().NotBeNull("the ManagedInstance row must be persisted");
        persisted!.OwnerId.Should().Be(user.Id);
        persisted.Domain.Should().Be("createhappy.xcord-dev.net");
    }

    [Fact]
    public async Task CreateInstance_ReservedSubdomain_RejectedByValidate()
    {
        await using var db = CreateDbContext();
        var handler = BuildHandler(db, UserIdBase + 2);

        // "admin" is on the reserved list in ValidationHelpers.
        var command = new CreateInstanceCommand("admin", "Reserved", InstanceTier.Free);
        var error = handler.Validate(command);

        error.Should().NotBeNull();
        error!.Code.Should().Be("VALIDATION_FAILED",
            "reserved infrastructure subdomains must be rejected before any DB work");
    }

    [Fact]
    public async Task CreateInstance_PaidTierWithoutStripe_RejectedByValidate()
    {
        await using var db = CreateDbContext();
        var handler = BuildHandler(db, UserIdBase + 3);

        var command = new CreateInstanceCommand("paidnostripe", "Paid", InstanceTier.Basic);
        var error = handler.Validate(command);

        error.Should().NotBeNull();
        error!.Code.Should().Be("PAID_TIER_UNAVAILABLE",
            "Stripe-not-configured must block any non-Free tier at validation time");
    }

    [Fact]
    public async Task CreateInstance_PaidServersDisabledFlag_BlocksPaidTierAtRuntime()
    {
        await using var db = CreateDbContext();
        var user = await SeedUserAsync(db, UserIdBase + 4, "psd_user");

        // Enable Stripe-configured options so Validate accepts the paid tier,
        // then trip the global PaidServersDisabled flag so Handle rejects it.
        var stripeOptions = Options.Create(new StripeOptions
        {
            SecretKey = "sk_test_fake_for_create_instance_test",
            PublishableKey = "pk_test_fake",
            WebhookSecret = string.Empty
        });
        await new SystemConfigService(db).SetPaidServersDisabledAsync(true, CancellationToken.None);

        var handler = BuildHandler(db, user.Id, stripeOptions);

        var command = new CreateInstanceCommand("paiddisabled", "Paid Disabled", InstanceTier.Basic);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("PAID_SERVERS_DISABLED",
            "the global PaidServersDisabled flag must short-circuit any paid creation");
    }
}
