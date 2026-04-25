using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Tests.Infrastructure;

public sealed class CheckSubdomainFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;
    private WebApplicationFactory<Program>? _factory;

    private const string EncryptionKey = "check-subdomain-encryption-key-256-bits-minimum-length-req!!";

    public WebApplicationFactory<Program> Factory => _factory!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("xcordhub_checksubdomain_test")
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
            ["Redis__ChannelPrefix"] = "checksubdomain-test",
            ["Jwt__Issuer"] = "checksubdomain-test",
            ["Jwt__Audience"] = "checksubdomain-test",
            ["Jwt__AccessTokenExpirationMinutes"] = "60",
            ["Encryption__Key"] = EncryptionKey,
            ["Docker__UseReal"] = "false",
            ["Caddy__UseReal"] = "false",
            ["Dns__Provider"] = "noop",
        };

        foreach (var (key, val) in envVars)
            Environment.SetEnvironmentVariable(key, val);

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseEnvironment("Development"));
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();

        var keys = new[] {
            "Database__ConnectionString", "Redis__ConnectionString", "Redis__ChannelPrefix",
            "Jwt__Issuer", "Jwt__Audience", "Jwt__AccessTokenExpirationMinutes",
            "Encryption__Key", "Docker__UseReal", "Caddy__UseReal", "Dns__Provider"
        };
        foreach (var key in keys)
            Environment.SetEnvironmentVariable(key, null);

        if (_redis is not null) await _redis.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    public HttpClient CreateAnonymousClient() => _factory!.CreateClient();

    public HttpClient CreateUserClient(long userId = 3_000_000_001L)
    {
        using var scope = _factory!.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtService>();
        var token = jwtService.GenerateAccessToken(userId, isAdmin: false);
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

[CollectionDefinition("CheckSubdomain")]
public class CheckSubdomainCollection : ICollectionFixture<CheckSubdomainFixture> { }

[Collection("CheckSubdomain")]
[Trait("Category", "Integration")]
public sealed class CheckSubdomainTests
{
    private readonly CheckSubdomainFixture _fixture;

    public CheckSubdomainTests(CheckSubdomainFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CheckSubdomain_Anonymous_ShouldNotRequireAuth()
    {
        var client = _fixture.CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/hub/check-subdomain?subdomain=testserver");

        response.StatusCode.Should().NotBe(
            HttpStatusCode.Unauthorized,
            "the Get Started wizard is used by unauthenticated users, so check-subdomain must allow anonymous access");
    }

    [Fact]
    public async Task CheckSubdomain_Authenticated_ReturnsOk()
    {
        var client = _fixture.CreateUserClient();

        var response = await client.GetAsync("/api/v1/hub/check-subdomain?subdomain=testserver");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
