using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using Xunit;

namespace XcordHub.Tests.Infrastructure.Fixtures;

/// <summary>
/// WebApplicationFactory-based fixture that starts the full XcordHub API
/// with isolated PostgreSQL and Redis containers via Testcontainers.
/// Used for HTTP-level integration tests that need real auth/authorization enforcement.
/// Set XCORD_INFRA_TESTS=1 to enable.
/// </summary>
public sealed class WebApiTestFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;
    private RedisContainer? _redisContainer;
    private WebApplicationFactory<Program>? _factory;

    public const string TestJwtIssuer = "xcord-hub-test";
    public const string TestJwtAudience = "xcord-hub-test-clients";
    public const string TestJwtSecretKey = "test-secret-key-with-minimum-256-bits-for-hmacsha256-abc";
    public const string TestEncryptionKey = "test-encryption-key-with-256-bits-minimum-length-required-here";

    private string _postgresConnectionString = string.Empty;
    private string _redisConnectionString = string.Empty;

    public bool IsEnabled { get; private set; }

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("XCORD_INFRA_TESTS") != "1")
        {
            Console.Error.WriteLine("[WebApiTestFixture] Skipped — set XCORD_INFRA_TESTS=1 to enable");
            return;
        }

        IsEnabled = true;

        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("xcordhub_webapi_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _postgresContainer.StartAsync();
        _postgresConnectionString = _postgresContainer.GetConnectionString();

        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await _redisContainer.StartAsync();
        _redisConnectionString = _redisContainer.GetConnectionString();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = _postgresConnectionString,
                        ["Redis:ConnectionString"] = _redisConnectionString,
                        ["Redis:ChannelPrefix"] = "webapi-test",
                        ["Jwt:Issuer"] = TestJwtIssuer,
                        ["Jwt:Audience"] = TestJwtAudience,
                        ["Jwt:SecretKey"] = TestJwtSecretKey,
                        ["Jwt:ExpirationMinutes"] = "60",
                        ["Encryption:Key"] = TestEncryptionKey,
                        ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
                        ["Docker:UseReal"] = "false",
                        ["Caddy:UseReal"] = "false",
                        ["Cloudflare:UseReal"] = "false"
                    });
                });
            });

        // Trigger host creation (runs EnsureCreated + seed)
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        if (_factory != null)
        {
            _factory.Dispose();
        }

        if (_redisContainer != null)
        {
            await _redisContainer.DisposeAsync();
        }

        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync();
        }
    }

    /// <summary>
    /// Creates an HttpClient with an admin JWT bearer token.
    /// </summary>
    public HttpClient CreateAdminClient(long userId = 1_000_000_001L)
    {
        EnsureEnabled();
        var jwtService = new JwtService(TestJwtIssuer, TestJwtAudience, TestJwtSecretKey, 60);
        var token = jwtService.GenerateAccessToken(userId, isAdmin: true);
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Creates an HttpClient with a regular (non-admin) JWT bearer token.
    /// </summary>
    public HttpClient CreateUserClient(long userId = 2_000_000_001L)
    {
        EnsureEnabled();
        var jwtService = new JwtService(TestJwtIssuer, TestJwtAudience, TestJwtSecretKey, 60);
        var token = jwtService.GenerateAccessToken(userId, isAdmin: false);
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Creates an HttpClient with no authorization header.
    /// </summary>
    public HttpClient CreateAnonymousClient()
    {
        EnsureEnabled();
        return _factory!.CreateClient();
    }

    /// <summary>
    /// Opens a fresh DbContext pointed at the test PostgreSQL instance.
    /// </summary>
    public HubDbContext CreateDbContext()
    {
        EnsureEnabled();
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_postgresConnectionString)
            .Options;
        return new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
    }

    private void EnsureEnabled()
    {
        if (!IsEnabled || _factory is null)
            throw new InvalidOperationException("WebApiTestFixture is not enabled. Set XCORD_INFRA_TESTS=1.");
    }
}

[CollectionDefinition("WebApi")]
public class WebApiCollection : ICollectionFixture<WebApiTestFixture> { }
