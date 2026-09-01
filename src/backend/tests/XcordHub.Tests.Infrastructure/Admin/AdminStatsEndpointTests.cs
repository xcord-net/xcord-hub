using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using XcordHub.Entities;
using XcordHub.Features.Admin;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Infrastructure.Fixtures;
using Xunit;

namespace XcordHub.Tests.Infrastructure.Admin;

/// <summary>
/// GET /api/v1/admin/stats end to end, against real PostgreSQL.
///
/// The route is reachable from the public internet - the guest's Caddyfile ends
/// in a catch-all that proxies anything unclaimed to the hub - so its three
/// defences are asserted here at the HTTP layer rather than only as unit tests
/// on the guard: a wrong token, an address off the tailnet, an unset token and a
/// caller over the rate limit each get a specific status, and none of them gets
/// the payload.
///
/// Owner IDs: 1_831_000_000 - 1_831_000_099
/// Instance IDs: 2_831_000_000 - 2_831_000_099
/// </summary>
[Collection("SharedPostgres")]
[Trait("Category", "Integration")]
public sealed class AdminStatsEndpointTests : IAsyncLifetime
{
    private const string TestEncryptionKey = "admin-stats-endpoint-tests-encryption-key-256-bits-req!!";
    private const string Token = "console-token-for-tests";
    private const string TailnetAddress = "100.64.0.3";
    private const long UserIdBase = 1_831_000_000L;
    private const long InstanceIdBase = 2_831_000_000L;

    private readonly SharedPostgresFixture _fixture;
    private string _connectionString = string.Empty;

    public AdminStatsEndpointTests(SharedPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture
            .CreateDatabaseAsync("xcordhub_admin_stats_endpoint_test", TestEncryptionKey);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HubDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
    }

    /// <summary>
    /// A host carrying just this endpoint and what it needs. The full API would
    /// bring Docker, Redis, MinIO and the RSA bootstrap along with it, none of
    /// which this route touches.
    /// </summary>
    private async Task<IHost> CreateHostAsync(string? statsToken = Token, int permitLimit = 30)
    {
        var connectionString = _connectionString;

        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            [StatsAccessGuard.TokenConfigurationKey] = statsToken,
                            ["Email:SmtpHost"] = "mail.xcord.net",
                            ["Email:FromAddress"] = "noreply@xcord.net",
                            ["Email:DevMode"] = "false"
                        }))
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddSingleton<IEncryptionService>(
                            new AesEncryptionService(TestEncryptionKey));
                        services.AddDbContext<HubDbContext>(o => o.UseNpgsql(connectionString));
                        services.AddScoped<GetAppStatsHandler>();
                        services.AddRateLimiter(options =>
                        {
                            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                            // The production shape: a GLOBAL limiter that matches
                            // on the path, not a named policy. Program.cs calls
                            // UseRateLimiter before UseRouting, so a policy
                            // attached with RequireRateLimiting is never read -
                            // which is why the middleware order below is the same
                            // way round as the real one.
                            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                                StatsAccessGuard.IsStatsRoute(context)
                                    ? RateLimitPartition.GetFixedWindowLimiter(
                                        StatsAccessGuard.RateLimitKey(context),
                                        _ => new FixedWindowRateLimiterOptions
                                        {
                                            PermitLimit = permitLimit,
                                            Window = TimeSpan.FromMinutes(1),
                                            QueueLimit = 0
                                        })
                                    : RateLimitPartition.GetNoLimiter<string>("other"));
                        });
                    })
                    .Configure(app =>
                    {
                        // Same order as Program.cs, deliberately: the limit above
                        // has to hold where the application actually puts this
                        // middleware, not where it would be easiest to test.
                        app.UseRateLimiter();
                        app.UseRouting();
                        app.UseEndpoints(endpoints => GetAppStatsHandler.Map(endpoints));
                    });
            })
            .StartAsync();
    }

    private static Task<HttpResponseMessage> GetStatsAsync(
        IHost host, string? token, string clientAddress = TailnetAddress)
    {
        var client = host.GetTestClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/stats");
        if (token is not null)
            request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("X-Real-IP", clientAddress);
        return client.SendAsync(request);
    }

    // --- the token ---------------------------------------------------------

    [Fact]
    public async Task NoToken_Returns401()
    {
        using var host = await CreateHostAsync();

        var response = await GetStatsAsync(host, token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WrongToken_Returns401()
    {
        using var host = await CreateHostAsync();

        var response = await GetStatsAsync(host, token: Token + "-wrong");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the console must be able to tell a bad token from an unreachable app");
    }

    [Fact]
    public async Task RightToken_Returns200_WithASchema2Envelope()
    {
        using var host = await CreateHostAsync();

        var response = await GetStatsAsync(host, Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payload.RootElement;

        root.GetProperty("schema").GetInt32().Should().Be(2);
        root.GetProperty("generated_at").GetString().Should().EndWith("Z");
        root.GetProperty("max_age_seconds").GetInt32().Should().BePositive();
        root.TryGetProperty("computed_ms", out _).Should().BeTrue();
        root.GetProperty("alerts").ValueKind.Should().Be(JsonValueKind.Array,
            "an empty alerts array is a positive statement and differs from omitting the field");

        // Every section computed against the real database, so a section carrying
        // `error` here means a query in the handler does not translate.
        foreach (var section in root.GetProperty("sections").EnumerateArray())
        {
            section.TryGetProperty("error", out var error).Should().BeFalse(
                "section '{0}' failed: {1}",
                section.GetProperty("title").GetString(),
                error.ValueKind == JsonValueKind.String ? error.GetString() : "");
        }
    }

    // --- the token being unset ---------------------------------------------

    [Fact]
    public async Task TokenNotConfigured_Returns503_NotThePayload()
    {
        using var host = await CreateHostAsync(statsToken: null);

        var response = await GetStatsAsync(host, Token);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "an unset secret must close the route, never open it");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("\"schema\"");
    }

    // --- the tailnet -------------------------------------------------------

    [Fact]
    public async Task RightTokenFromOffTheTailnet_Returns403()
    {
        using var host = await CreateHostAsync();

        // What a request through the public ingress looks like: nginx sets
        // X-Real-IP to the peer it saw, and that is not a tailnet address.
        var response = await GetStatsAsync(host, Token, clientAddress: "203.0.113.7");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SpoofedForwardedForFromOffTheTailnet_Returns403()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/stats");
        request.Headers.Add("Authorization", $"Bearer {Token}");
        request.Headers.Add("X-Forwarded-For", "100.64.0.9, 203.0.113.7");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the relay appends the address it saw, so a forged leftmost entry cannot hide it");
    }

    // --- the rate limit -----------------------------------------------------

    [Fact]
    public async Task OverTheRateLimit_Returns429()
    {
        using var host = await CreateHostAsync(permitLimit: 2);

        (await GetStatsAsync(host, Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetStatsAsync(host, Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetStatsAsync(host, Token)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "the limit is what stops the token being brute-forced for free");
    }

    // --- the payload --------------------------------------------------------

    [Fact]
    public async Task AnInstanceStuckInProvisioning_IsReportedAsACriticalAlert()
    {
        await using (var db = CreateDbContext())
        {
            await SeedInstanceAsync(
                db,
                UserIdBase + 1,
                InstanceIdBase + 1,
                "stuck",
                InstanceStatus.Provisioning,
                lastAttemptAt: DateTimeOffset.UtcNow.AddMinutes(-25));
        }

        using var host = await CreateHostAsync();
        var response = await GetStatsAsync(host, Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var alert = payload.RootElement.GetProperty("alerts").EnumerateArray()
            .Single(a => a.GetProperty("code").GetString() == "instances_provisioning_stuck");

        alert.GetProperty("severity").GetString().Should().Be("crit");
        alert.GetProperty("count").GetInt32().Should().Be(1);
        alert.GetProperty("oldest_age_s").GetInt64().Should().BeGreaterThan(600);
        alert.GetProperty("detail").EnumerateArray().Single().GetString()
            .Should().Contain("stuck.xcord-dev.net");
    }

    [Fact]
    public async Task PostureReportsStripeAndAlertingAsUnconfigured()
    {
        using var host = await CreateHostAsync();

        var response = await GetStatsAsync(host, Token);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var posture = payload.RootElement.GetProperty("posture").EnumerateArray().ToList();

        posture.Single(p => p.GetProperty("label").GetString() == "Stripe")
            .GetProperty("state").GetString().Should().Be("crit",
                "no secret key means the webhook answers 503 and usage is never reported");
        posture.Single(p => p.GetProperty("label").GetString() == "Health alerting")
            .GetProperty("state").GetString().Should().Be("crit",
                "an unset webhook URL means every 5-failure alarm is logged and dropped");
    }

    private static async Task<ManagedInstance> SeedInstanceAsync(
        HubDbContext db,
        long userId,
        long instanceId,
        string subdomain,
        InstanceStatus status,
        DateTimeOffset? lastAttemptAt = null)
    {
        var enc = new AesEncryptionService(TestEncryptionKey);
        db.HubUsers.Add(new HubUser
        {
            Id = userId,
            Username = $"stats_{subdomain}",
            DisplayName = $"Stats {subdomain}",
            Email = enc.Encrypt($"stats_{subdomain}@test.invalid"),
            EmailHash = enc.ComputeHmac($"stats_{subdomain}@test.invalid"),
            PasswordHash = "hashed",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var instance = new ManagedInstance
        {
            Id = instanceId,
            OwnerId = userId,
            Domain = $"{subdomain}.xcord-dev.net",
            DisplayName = $"Stats instance {subdomain}",
            Status = status,
            SnowflakeWorkerId = (int)(instanceId - InstanceIdBase + 831),
            ProvisioningAttempts = 1,
            LastProvisioningAttemptAt = lastAttemptAt,
            CreatedAt = lastAttemptAt ?? DateTimeOffset.UtcNow,
            Billing = new InstanceBilling
            {
                Tier = InstanceTier.Free,
                MediaEnabled = false,
                BillingStatus = BillingStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            }
        };
        db.ManagedInstances.Add(instance);
        await db.SaveChangesAsync();
        return instance;
    }
}
