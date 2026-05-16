using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Features.Auth;
using XcordHub.Features.Billing;
using XcordHub.Features.Instances;
using XcordHub.Features.Provisioning;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Infrastructure.Fixtures;

namespace XcordHub.Tests.Infrastructure.Billing;

/// <summary>
/// Shared base for the hub billing integration test suite (cards 126).
/// Each subclass focuses on one feature area (GetBilling / ChangePlan /
/// CancelBilling / Invoices / Webhook) and reuses these helpers.
/// Each concrete subclass must still carry <c>[Collection("SharedPostgres")]</c>
/// because xUnit does not inherit the attribute.
///
/// ID ranges reserved for this class hierarchy:
///   User IDs:     1_255_000_000 – 1_255_000_099
///   Instance IDs: 2_255_000_000 – 2_255_000_099  (assigned by Snowflake; verified by DB query)
/// </summary>
public abstract class BillingTestsBase
{
    protected const string TestEncryptionKey = "billing-tests-encryption-key-with-256-bits-minimum-okk!";
    protected const long UserIdBase = 1_255_000_000L;

    protected readonly string _connectionString;

    protected BillingTestsBase(SharedPostgresFixture fixture, string dbName)
    {
        _connectionString = fixture.CreateDatabaseAsync(dbName, TestEncryptionKey).GetAwaiter().GetResult();
    }

    protected HubDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
    }

    protected static IConfiguration BuildConfiguration(string? baseUrl = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hub:BaseDomain"] = "xcord-dev.net",
                ["Hub:BaseUrl"] = baseUrl ?? "https://xcord-dev.net"
            })
            .Build();

    /// <summary>
    /// Returns a StripeOptions wrapper that reports Stripe as NOT configured.
    /// </summary>
    protected static IOptions<StripeOptions> NoStripeOptions() =>
        Microsoft.Extensions.Options.Options.Create(new StripeOptions());

    /// <summary>
    /// Returns a StripeOptions wrapper that reports Stripe as configured.
    /// The key is fake; no real API calls are made because the stub IStripeService
    /// is injected instead of the real StripeService.
    /// </summary>
    protected static IOptions<StripeOptions> FakeStripeOptions() =>
        Microsoft.Extensions.Options.Options.Create(new StripeOptions
        {
            SecretKey = "sk_test_fake_key_for_unit_testing",
            PublishableKey = "pk_test_fake",
            WebhookSecret = string.Empty
        });

    protected static ICurrentUserService StubUser(long userId) =>
        new FixedCurrentUserService(userId);

    /// <summary>
    /// Seeds a HubUser and a ManagedInstance (with Billing + Config) and returns the instance ID.
    /// The provisioning queue is a no-op so no containers are launched.
    /// </summary>
    protected async Task<(HubUser user, long instanceId)> SeedInstanceAsync(
        HubDbContext dbContext,
        long userId,
        string usernameSuffix,
        InstanceTier tier = InstanceTier.Free,
        bool mediaEnabled = false)
    {
        var encryptionService = new AesEncryptionService(TestEncryptionKey);
        var user = new HubUser
        {
            Id = userId,
            Username = $"bu_{usernameSuffix}"[..Math.Min(32, $"bu_{usernameSuffix}".Length)],
            DisplayName = $"BU {usernameSuffix}"[..Math.Min(32, $"BU {usernameSuffix}".Length)],
            Email = encryptionService.Encrypt($"bu_{usernameSuffix}@test.invalid"),
            EmailHash = encryptionService.ComputeHmac($"bu_{usernameSuffix}@test.invalid"),
            PasswordHash = "hashed_password",
            IsAdmin = false,
            IsDisabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = DateTimeOffset.UtcNow
        };
        dbContext.HubUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var creationService = new InstanceCreationService(
            dbContext,
            new NoOpCaptchaService(),
            new SnowflakeIdGenerator(255),
            BuildConfiguration(),
            Options.Create(new AuthOptions()));

        var handler = new CreateInstanceHandler(
            dbContext,
            StubUser(userId),
            new NoOpProvisioningQueue(),
            creationService,
            new SystemConfigService(dbContext),
            NoStripeOptions());

        // Subdomain must be lowercase alphanumeric with hyphens only (no underscores).
        var subdomain = $"bt-{usernameSuffix}".Replace("_", "-").ToLowerInvariant();

        var result = await handler.Handle(
            new CreateInstanceCommand(subdomain, $"Billing Test {usernameSuffix}", tier, mediaEnabled),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue("test setup: instance creation must succeed");
        var instanceId = long.Parse(result.Value.InstanceId);

        return (user, instanceId);
    }
}

// ---------------------------------------------------------------------------
// Shared stubs - internal so they are reusable across BillingTests partials,
// but scoped to the billing test namespace.
// ---------------------------------------------------------------------------

internal sealed class FixedCurrentUserService : ICurrentUserService
{
    private readonly long _userId;
    public FixedCurrentUserService(long userId) => _userId = userId;
    public Result<long> GetCurrentUserId() => Result<long>.Success(_userId);
}

internal sealed class NoOpProvisioningQueue : IProvisioningQueue
{
    public Task EnqueueAsync(long instanceId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<long?> DequeueAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<long?>(null);

    public Task<List<long>> GetPendingInstancesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new List<long>());
}

/// <summary>
/// IStripeService stub that does nothing - used when Stripe is not configured
/// and no API calls are expected.
/// </summary>
internal sealed class NoOpStripeService : IStripeService
{
    public Task<string> EnsureCustomerAsync(long userId, string email, string displayName, CancellationToken ct = default)
        => Task.FromResult("cus_noop");

    public Task<CheckoutResult> CreateCheckoutSessionAsync(CreateCheckoutRequest request, CancellationToken ct = default)
        => Task.FromResult(new CheckoutResult("cs_noop", "https://example.com/checkout/noop"));

    public Task<SetupIntentResult> CreateSetupIntentAsync(Dictionary<string, string>? metadata = null, CancellationToken ct = default)
        => Task.FromResult(new SetupIntentResult("seti_noop", "seti_noop_secret"));

    public Task<string?> ResolvePriceIdByLookupKeyAsync(string lookupKey, CancellationToken ct = default)
        => Task.FromResult<string?>($"price_resolved_{lookupKey}");

    public Task<CreateSubscriptionResult> CreateSubscriptionAsync(string customerId, string priceId, string paymentMethodId, int trialDays = 0, Dictionary<string, string>? metadata = null, CancellationToken ct = default)
        => Task.FromResult(new CreateSubscriptionResult("sub_noop", "in_noop"));

    public Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<List<StripeInvoice>> GetInvoicesAsync(string customerId, int limit = 25, CancellationToken ct = default)
        => Task.FromResult(new List<StripeInvoice>());

    public Task ReportUsageAsync(string subscriptionItemId, long minutesUptime, DateTimeOffset timestamp, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<CreateMeteredSubscriptionResult> CreateMeteredSubscriptionAsync(string customerId, string meteredPriceId, string paymentMethodId, int trialDays = 0, Dictionary<string, string>? metadata = null, CancellationToken ct = default)
        => Task.FromResult(new CreateMeteredSubscriptionResult("sub_noop", "si_noop", null));
}

/// <summary>
/// IStripeService spy that records which methods were called and with what arguments.
/// </summary>
internal sealed class SpyStripeService : IStripeService
{
    private readonly string _checkoutUrl;
    private readonly List<StripeInvoice> _invoices;

    public bool EnsureCustomerCalled { get; private set; }
    public bool CreateCheckoutCalled { get; private set; }
    public bool CancelSubscriptionCalled { get; private set; }
    public bool GetInvoicesCalled { get; private set; }
    public string? LastCancelledSubscriptionId { get; private set; }
    public string? LastGetInvoicesCustomerId { get; private set; }

    public SpyStripeService(string checkoutUrl, List<StripeInvoice>? invoices = null)
    {
        _checkoutUrl = checkoutUrl;
        _invoices = invoices ?? new List<StripeInvoice>();
    }

    public Task<string> EnsureCustomerAsync(long userId, string email, string displayName, CancellationToken ct = default)
    {
        EnsureCustomerCalled = true;
        return Task.FromResult("cus_spy_test");
    }

    public Task<CheckoutResult> CreateCheckoutSessionAsync(CreateCheckoutRequest request, CancellationToken ct = default)
    {
        CreateCheckoutCalled = true;
        return Task.FromResult(new CheckoutResult("cs_spy_test", _checkoutUrl));
    }

    public Task<SetupIntentResult> CreateSetupIntentAsync(Dictionary<string, string>? metadata = null, CancellationToken ct = default)
        => Task.FromResult(new SetupIntentResult("seti_spy_test", "seti_spy_test_secret"));

    public Task<string?> ResolvePriceIdByLookupKeyAsync(string lookupKey, CancellationToken ct = default)
        => Task.FromResult<string?>($"price_resolved_{lookupKey}");

    public Task<CreateSubscriptionResult> CreateSubscriptionAsync(string customerId, string priceId, string paymentMethodId, int trialDays = 0, Dictionary<string, string>? metadata = null, CancellationToken ct = default)
        => Task.FromResult(new CreateSubscriptionResult("sub_spy_test", "in_spy_test"));

    public Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
    {
        CancelSubscriptionCalled = true;
        LastCancelledSubscriptionId = subscriptionId;
        return Task.CompletedTask;
    }

    public Task<List<StripeInvoice>> GetInvoicesAsync(string customerId, int limit = 25, CancellationToken ct = default)
    {
        GetInvoicesCalled = true;
        LastGetInvoicesCustomerId = customerId;
        return Task.FromResult(_invoices);
    }

    public Task ReportUsageAsync(string subscriptionItemId, long minutesUptime, DateTimeOffset timestamp, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<CreateMeteredSubscriptionResult> CreateMeteredSubscriptionAsync(string customerId, string meteredPriceId, string paymentMethodId, int trialDays = 0, Dictionary<string, string>? metadata = null, CancellationToken ct = default)
        => Task.FromResult(new CreateMeteredSubscriptionResult("sub_spy", "si_spy", null));
}
