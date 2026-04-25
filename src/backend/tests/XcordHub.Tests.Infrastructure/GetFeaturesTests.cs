using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using XcordHub.Features.Config;

namespace XcordHub.Tests.Infrastructure;

// ── Fixture ──────────────────────────────────────────────────────────────────

public sealed class FeaturesEndpointFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;
    private WebApplicationFactory<Program>? _withStripe;
    private WebApplicationFactory<Program>? _withoutStripe;

    public const string TestEncryptionKey = "features-ep-encryption-key-256-bits-minimum-length-required!!";

    public WebApplicationFactory<Program> WithStripeFactory => _withStripe!;
    public WebApplicationFactory<Program> WithoutStripeFactory => _withoutStripe!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("xcordhub_features_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await _postgres.StartAsync();

        _redis = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
        await _redis.StartAsync();

        var baseEnv = new Dictionary<string, string?>
        {
            ["Database__ConnectionString"] = _postgres.GetConnectionString(),
            ["Redis__ConnectionString"] = _redis.GetConnectionString(),
            ["Redis__ChannelPrefix"] = "features-test",
            ["Jwt__Issuer"] = "features-test",
            ["Jwt__Audience"] = "features-test",
            ["Jwt__AccessTokenExpirationMinutes"] = "60",
            ["Encryption__Key"] = TestEncryptionKey,
            ["Docker__UseReal"] = "false",
            ["Caddy__UseReal"] = "false",
            ["Dns__Provider"] = "noop",
        };

        // Set base env vars
        foreach (var (key, val) in baseEnv)
            Environment.SetEnvironmentVariable(key, val);

        // Factory WITH Stripe key
        Environment.SetEnvironmentVariable("Stripe__SecretKey", "sk_test_features_test_key");
        _withStripe = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseEnvironment("Development"));
        _ = _withStripe.Server;

        // Factory WITHOUT Stripe key - need a separate factory with different config
        Environment.SetEnvironmentVariable("Stripe__SecretKey", null);
        _withoutStripe = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Stripe:SecretKey"] = "",
                    });
                });
            });
        _ = _withoutStripe.Server;
    }

    public async Task DisposeAsync()
    {
        _withStripe?.Dispose();
        _withoutStripe?.Dispose();

        Environment.SetEnvironmentVariable("Database__ConnectionString", null);
        Environment.SetEnvironmentVariable("Redis__ConnectionString", null);
        Environment.SetEnvironmentVariable("Redis__ChannelPrefix", null);
        Environment.SetEnvironmentVariable("Jwt__Issuer", null);
        Environment.SetEnvironmentVariable("Jwt__Audience", null);
        Environment.SetEnvironmentVariable("Jwt__AccessTokenExpirationMinutes", null);
        Environment.SetEnvironmentVariable("Encryption__Key", null);
        Environment.SetEnvironmentVariable("Docker__UseReal", null);
        Environment.SetEnvironmentVariable("Caddy__UseReal", null);
        Environment.SetEnvironmentVariable("Dns__Provider", null);
        Environment.SetEnvironmentVariable("Stripe__SecretKey", null);

        if (_redis is not null) await _redis.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }
}

[CollectionDefinition("FeaturesEndpoint")]
public class FeaturesEndpointCollection : ICollectionFixture<FeaturesEndpointFixture> { }

// ── Tests ────────────────────────────────────────────────────────────────────

[Collection("FeaturesEndpoint")]
[Trait("Category", "Integration")]
public sealed class GetFeaturesEndpointTests
{
    private readonly FeaturesEndpointFixture _fixture;

    public GetFeaturesEndpointTests(FeaturesEndpointFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetFeatures_WithStripeKey_ReturnsPaymentsEnabled()
    {
        var client = _fixture.WithStripeFactory.CreateClient();

        var response = await client.GetAsync("/api/v1/hub/features");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetFeaturesResponse>();
        body.Should().NotBeNull();
        body!.PaymentsEnabled.Should().BeTrue("Stripe SecretKey is configured");
    }

    [Fact]
    public async Task GetFeatures_WithoutStripeKey_ReturnsPaymentsDisabled()
    {
        var client = _fixture.WithoutStripeFactory.CreateClient();

        var response = await client.GetAsync("/api/v1/hub/features");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetFeaturesResponse>();
        body.Should().NotBeNull();
        body!.PaymentsEnabled.Should().BeFalse("no Stripe SecretKey is configured");
    }

    [Fact]
    public async Task GetFeatures_IsAnonymous_NoAuthRequired()
    {
        var client = _fixture.WithStripeFactory.CreateClient();
        // No Authorization header set

        var response = await client.GetAsync("/api/v1/hub/features");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "endpoint should allow anonymous access");
    }

    [Fact]
    public async Task GetFeatures_ResponseIsCamelCase()
    {
        var client = _fixture.WithStripeFactory.CreateClient();

        var response = await client.GetAsync("/api/v1/hub/features");
        var json = await response.Content.ReadAsStringAsync();

        json.Should().Contain("\"paymentsEnabled\"", "response should use camelCase property names");
        json.Should().NotContain("\"PaymentsEnabled\"", "response should not use PascalCase");
    }
}
