using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Tests.Infrastructure;

/// <summary>
/// Tests that paid tier instance creation requires a valid paymentMethodId,
/// and that free tier creation works without one.
/// Uses a mock IStripeService so no real Stripe keys are needed.
/// </summary>
public sealed class BillingFlowFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;
    private WebApplicationFactory<Program>? _factory;

    private const string EncryptionKey = "billing-flow-encryption-key-256-bits-minimum-length-req!!!!";
    private const string StripeSecretKey = "sk_test_fake_billing_flow_test_key";

    public WebApplicationFactory<Program> Factory => _factory!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("xcordhub_billing_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await _postgres.StartAsync();

        _redis = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
        await _redis.StartAsync();

        var envVars = new Dictionary<string, string?>
        {
            ["Database__ConnectionString"] = _postgres.GetConnectionString(),
            ["Redis__ConnectionString"] = _redis.GetConnectionString(),
            ["Redis__ChannelPrefix"] = "billing-test",
            ["Jwt__Issuer"] = "billing-test",
            ["Jwt__Audience"] = "billing-test",
            ["Jwt__AccessTokenExpirationMinutes"] = "60",
            ["Encryption__Key"] = EncryptionKey,
            ["Docker__UseReal"] = "false",
            ["Caddy__UseReal"] = "false",
            ["Dns__Provider"] = "noop",
            ["Stripe__SecretKey"] = StripeSecretKey,
            ["Stripe__PublishableKey"] = "pk_test_fake",
            ["Captcha__Enabled"] = "false",
        };

        foreach (var (key, val) in envVars)
            Environment.SetEnvironmentVariable(key, val);

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.ConfigureServices(services =>
                {
                    services.AddScoped<IStripeService, FakeStripeService>();
                });
            });
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();

        var keys = new[]
        {
            "Database__ConnectionString", "Redis__ConnectionString", "Redis__ChannelPrefix",
            "Jwt__Issuer", "Jwt__Audience", "Jwt__AccessTokenExpirationMinutes",
            "Encryption__Key", "Docker__UseReal", "Caddy__UseReal", "Dns__Provider",
            "Stripe__SecretKey", "Stripe__PublishableKey", "Captcha__Enabled"
        };
        foreach (var key in keys)
            Environment.SetEnvironmentVariable(key, null);

        if (_redis is not null) await _redis.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    public HttpClient CreateAnonymousClient() => _factory!.CreateClient();
}

internal sealed class FakeStripeService : IStripeService
{
    public Task<string> EnsureCustomerAsync(long userId, string email, string displayName, CancellationToken ct = default)
        => Task.FromResult($"cus_fake_{userId}");

    public Task<CheckoutResult> CreateCheckoutSessionAsync(CreateCheckoutRequest request, CancellationToken ct = default)
        => Task.FromResult(new CheckoutResult($"cs_fake_{request.InstanceId}", $"https://checkout.stripe.com/fake/{request.InstanceId}"));

    public Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<List<StripeInvoice>> GetInvoicesAsync(string customerId, int limit = 25, CancellationToken ct = default)
        => Task.FromResult(new List<StripeInvoice>());

    public Task<SetupIntentResult> CreateSetupIntentAsync(Dictionary<string, string>? metadata = null, CancellationToken ct = default)
        => Task.FromResult(new SetupIntentResult("seti_fake_123", "seti_fake_123_secret_abc"));

    public Task<string?> ResolvePriceIdByLookupKeyAsync(string lookupKey, CancellationToken ct = default)
        => Task.FromResult<string?>($"price_resolved_{lookupKey}");

    public Task<CreateSubscriptionResult> CreateSubscriptionAsync(string customerId, string priceId, string paymentMethodId, int trialDays = 0, Dictionary<string, string>? metadata = null, CancellationToken ct = default)
        => Task.FromResult(new CreateSubscriptionResult("sub_fake_123", "in_fake_123"));

    public Task ReportUsageAsync(string subscriptionItemId, long minutesUptime, DateTimeOffset timestamp, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<CreateMeteredSubscriptionResult> CreateMeteredSubscriptionAsync(string customerId, string meteredPriceId, string paymentMethodId, int trialDays = 0, Dictionary<string, string>? metadata = null, CancellationToken ct = default)
        => Task.FromResult(new CreateMeteredSubscriptionResult("sub_fake_metered", "si_fake_metered", null));
}

[CollectionDefinition("BillingFlow")]
public class BillingFlowCollection : ICollectionFixture<BillingFlowFixture> { }

[Collection("BillingFlow")]
[Trait("Category", "Integration")]
public sealed class BillingFlowTests
{
    private readonly BillingFlowFixture _fixture;

    public BillingFlowTests(BillingFlowFixture fixture) => _fixture = fixture;

    // ── Payment intent creation ────────────────────────────────────────────

    [Fact]
    public async Task CreatePaymentIntent_PaidTier_ReturnsClientSecret()
    {
        var client = _fixture.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/hub/billing/create-payment-intent", new
        {
            tier = "Basic",
            mediaEnabled = false,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body.Should().ContainKey("clientSecret",
            "payment intent endpoint must return a Stripe client secret for the Payment Element");
        body.Should().ContainKey("priceCents");
    }

    [Fact]
    public async Task CreatePaymentIntent_FreeTier_Rejected()
    {
        var client = _fixture.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/hub/billing/create-payment-intent", new
        {
            tier = "Free",
            mediaEnabled = false,
        });

        response.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "free tier should not create a payment intent - no payment needed");
    }

    // ── Registration with payment ──────────────────────────────────────────

    [Fact]
    public async Task RegisterWithInstance_PaidTier_WithoutPaymentIntentId_Rejected()
    {
        var client = _fixture.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/hub/register-with-instance", new
        {
            username = "billinguser1",
            displayName = "Billing User 1",
            email = "billing1@test.com",
            password = "TestPassword123!",
            subdomain = "billing-test-one",
            instanceDisplayName = "Billing Test 1",
            tier = "Basic",
            mediaEnabled = false,
            // No paymentMethodId provided
        });

        // Should be rejected - paid tier requires payment
        response.StatusCode.Should().NotBe(HttpStatusCode.Created,
            "paid tier registration without a paymentMethodId must be rejected");
    }

    [Fact]
    public async Task RegisterWithInstance_PaidTier_WithPaymentIntentId_Succeeds()
    {
        var client = _fixture.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/hub/register-with-instance", new
        {
            username = "billinguser2",
            displayName = "Billing User 2",
            email = "billing2@test.com",
            password = "TestPassword123!",
            subdomain = "billing-test-two",
            instanceDisplayName = "Billing Test 2",
            tier = "Pro",
            mediaEnabled = true,
            paymentMethodId = "pi_fake_123",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "paid tier registration with a valid paymentMethodId should succeed");

        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body.Should().ContainKey("instanceId");
        body!["status"].ToString().Should().Be("Pending",
            "paid instance with completed payment should be queued for provisioning");
    }

    [Fact]
    public async Task RegisterWithInstance_FreeTier_NoPaymentRequired()
    {
        var client = _fixture.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/hub/register-with-instance", new
        {
            username = "freeuser1",
            displayName = "Free User 1",
            email = "free1@test.com",
            password = "TestPassword123!",
            subdomain = "free-test-one",
            instanceDisplayName = "Free Test 1",
            tier = "Free",
            mediaEnabled = false,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "free tier should not require payment");

        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body!["status"].ToString().Should().Be("Pending",
            "free tier instance should be queued for provisioning immediately");
    }
}
