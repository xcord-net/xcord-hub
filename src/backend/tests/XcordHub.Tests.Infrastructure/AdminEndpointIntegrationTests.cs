using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Tests.Infrastructure;

/// <summary>
/// Shared fixture for AdminEndpointIntegrationTests.
/// Starts one PostgreSQL container, one Redis container, and one WebApplicationFactory
/// for all tests in the collection. Environment variables provide config to the minimal-API
/// program before host construction (required because WebApplicationFactory with minimal
/// APIs reads config during service registration, before ConfigureAppConfiguration runs).
/// </summary>
public sealed class AdminEndpointFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;
    private WebApplicationFactory<Program>? _factory;

    public const string TestJwtIssuer = "xcord-hub-adminep-test";
    public const string TestJwtAudience = "xcord-hub-adminep-clients";
    public const string TestJwtSecretKey = "adminep-test-secret-key-with-minimum-256-bits-for-hmacsha256";
    public const string TestEncryptionKey = "adminep-encryption-key-with-256-bits-minimum-length-required!!";

    public string ConnectionString { get; private set; } = string.Empty;
    public WebApplicationFactory<Program> Factory => _factory!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("xcordhub_adminep_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();

        _redis = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await _redis.StartAsync();

        // WebApplicationFactory with minimal-API programs reads configuration during host
        // construction (before ConfigureAppConfiguration callbacks run). Provide config via
        // environment variables so the Program.cs validation guards do not throw.
        Environment.SetEnvironmentVariable("Database__ConnectionString", ConnectionString);
        Environment.SetEnvironmentVariable("Redis__ConnectionString", _redis.GetConnectionString());
        Environment.SetEnvironmentVariable("Redis__ChannelPrefix", "adminep-test");
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestJwtIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestJwtAudience);
        Environment.SetEnvironmentVariable("Jwt__SecretKey", TestJwtSecretKey);
        Environment.SetEnvironmentVariable("Jwt__ExpirationMinutes", "60");
        Environment.SetEnvironmentVariable("Encryption__Key", TestEncryptionKey);
        Environment.SetEnvironmentVariable("Docker__UseReal", "false");
        Environment.SetEnvironmentVariable("Caddy__UseReal", "false");
        Environment.SetEnvironmentVariable("Cloudflare__UseReal", "false");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
            });

        // Trigger host creation — this runs EnsureCreatedAsync on the schema.
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();

        // Clear environment variables so they do not leak into other test sessions.
        Environment.SetEnvironmentVariable("Database__ConnectionString", null);
        Environment.SetEnvironmentVariable("Redis__ConnectionString", null);
        Environment.SetEnvironmentVariable("Redis__ChannelPrefix", null);
        Environment.SetEnvironmentVariable("Jwt__Issuer", null);
        Environment.SetEnvironmentVariable("Jwt__Audience", null);
        Environment.SetEnvironmentVariable("Jwt__SecretKey", null);
        Environment.SetEnvironmentVariable("Jwt__ExpirationMinutes", null);
        Environment.SetEnvironmentVariable("Encryption__Key", null);
        Environment.SetEnvironmentVariable("Docker__UseReal", null);
        Environment.SetEnvironmentVariable("Caddy__UseReal", null);
        Environment.SetEnvironmentVariable("Cloudflare__UseReal", null);

        if (_redis is not null)
            await _redis.DisposeAsync();

        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }
}

[CollectionDefinition("AdminEndpoint")]
public class AdminEndpointCollection : ICollectionFixture<AdminEndpointFixture> { }

/// <summary>
/// HTTP integration tests for the admin instances endpoints.
/// Verifies that GET /api/v1/admin/instances and GET /api/v1/admin/instances/{id}
/// enforce authorization correctly and return the expected response schema.
/// Uses Testcontainers (PostgreSQL + Redis) and WebApplicationFactory.
/// </summary>
[Collection("AdminEndpoint")]
[Trait("Category", "Integration")]
public sealed class AdminEndpointIntegrationTests
{
    private readonly AdminEndpointFixture _fixture;

    public AdminEndpointIntegrationTests(AdminEndpointFixture fixture)
    {
        _fixture = fixture;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HubDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        return new HubDbContext(options, new AesEncryptionService(AdminEndpointFixture.TestEncryptionKey));
    }

    private HttpClient CreateAdminClient()
    {
        var jwtService = new JwtService(
            AdminEndpointFixture.TestJwtIssuer,
            AdminEndpointFixture.TestJwtAudience,
            AdminEndpointFixture.TestJwtSecretKey,
            60);
        var token = jwtService.GenerateAccessToken(1_000_000_001L, isAdmin: true);
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient CreateUserClient()
    {
        var jwtService = new JwtService(
            AdminEndpointFixture.TestJwtIssuer,
            AdminEndpointFixture.TestJwtAudience,
            AdminEndpointFixture.TestJwtSecretKey,
            60);
        var token = jwtService.GenerateAccessToken(2_000_000_001L, isAdmin: false);
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient CreateAnonymousClient() => _fixture.Factory.CreateClient();

    private static HubUser MakeOwner(long id, string username) => new HubUser
    {
        Id = id,
        Username = username,
        DisplayName = username,
        Email = Encoding.UTF8.GetBytes($"encrypted-{username}@test.invalid"),
        EmailHash = Encoding.UTF8.GetBytes($"hash-{username}"),
        PasswordHash = "hashed",
        IsAdmin = false,
        IsDisabled = false,
        CreatedAt = DateTimeOffset.UtcNow,
        LastLoginAt = DateTimeOffset.UtcNow
    };

    // ── GET /api/v1/admin/instances — authorization ───────────────────────────

    [Fact]
    public async Task ListInstances_WithAdminToken_Returns200()
    {
        // Arrange
        using var client = CreateAdminClient();

        // Act
        var response = await client.GetAsync("/api/v1/admin/instances?page=1&pageSize=25");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListInstances_WithNonAdminToken_Returns403()
    {
        // Arrange
        using var client = CreateUserClient();

        // Act
        var response = await client.GetAsync("/api/v1/admin/instances?page=1&pageSize=25");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListInstances_WithNoToken_Returns401()
    {
        // Arrange
        using var client = CreateAnonymousClient();

        // Act
        var response = await client.GetAsync("/api/v1/admin/instances?page=1&pageSize=25");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── GET /api/v1/admin/instances — paginated response schema ──────────────

    [Fact]
    public async Task ListInstances_WithAdminToken_ReturnsPaginatedResponseSchema()
    {
        // Arrange
        using var client = CreateAdminClient();

        // Act
        var response = await client.GetAsync("/api/v1/admin/instances?page=1&pageSize=10");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AdminListInstancesResponseDto>();

        // Assert — verify the paginated response schema
        body.Should().NotBeNull();
        body!.Instances.Should().NotBeNull();
        body.Page.Should().Be(1);
        body.PageSize.Should().Be(10);
        body.Total.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ListInstances_WithRunningInstance_InstanceAppearsInResponse()
    {
        // Arrange — seed a running instance with a known domain
        await using var dbContext = CreateDbContext();

        var owner = MakeOwner(8_100_000_001L, "adminep-list-owner");
        dbContext.HubUsers.Add(owner);

        var instance = new ManagedInstance
        {
            Id = 8_100_000_002L,
            OwnerId = owner.Id,
            Domain = "adminep-list.xcord.net",
            DisplayName = "Admin EP List Instance",
            Status = InstanceStatus.Running,
            SnowflakeWorkerId = 810,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ManagedInstances.Add(instance);
        await dbContext.SaveChangesAsync();

        using var client = CreateAdminClient();

        // Act
        var response = await client.GetAsync("/api/v1/admin/instances?page=1&pageSize=100");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AdminListInstancesResponseDto>();

        // Assert
        body.Should().NotBeNull();
        var dto = body!.Instances.FirstOrDefault(i => i.Id == instance.Id);
        dto.Should().NotBeNull("the seeded instance should appear in the admin list");
        dto!.DisplayName.Should().Be("Admin EP List Instance");
        dto.Status.Should().Be("Running");
        dto.OwnerUsername.Should().Be("adminep-list-owner");
        dto.Subdomain.Should().Be("adminep-list");
    }

    [Fact]
    public async Task ListInstances_PaginationDefaults_WhenPageAndSizeAreZero()
    {
        // Arrange — the handler clamps page=0 to 1 and pageSize=0 to 25
        using var client = CreateAdminClient();

        // Act — send page=0, pageSize=0 so the handler applies defaults
        var response = await client.GetAsync("/api/v1/admin/instances?page=0&pageSize=0");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AdminListInstancesResponseDto>();

        // Assert
        body.Should().NotBeNull();
        body!.Page.Should().Be(1, "page 0 should be clamped to 1");
        body.PageSize.Should().Be(25, "pageSize 0 should default to 25");
    }

    // ── GET /api/v1/admin/instances/{id} — authorization and schema ───────────

    [Fact]
    public async Task GetInstance_WithAdminToken_Returns200AndInstanceDetails()
    {
        // Arrange — seed an instance
        await using var dbContext = CreateDbContext();

        var owner = MakeOwner(8_200_000_001L, "adminep-get-owner");
        dbContext.HubUsers.Add(owner);

        var instance = new ManagedInstance
        {
            Id = 8_200_000_002L,
            OwnerId = owner.Id,
            Domain = "adminep-get.xcord.net",
            DisplayName = "Admin EP Get Instance",
            Status = InstanceStatus.Running,
            SnowflakeWorkerId = 820,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ManagedInstances.Add(instance);
        await dbContext.SaveChangesAsync();

        using var client = CreateAdminClient();

        // Act
        var response = await client.GetAsync($"/api/v1/admin/instances/{instance.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<AdminGetInstanceResponseDto>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(instance.Id);
        body.DisplayName.Should().Be("Admin EP Get Instance");
        body.Domain.Should().Be("adminep-get.xcord.net");
        body.Subdomain.Should().Be("adminep-get");
        body.Status.Should().Be("Running");
        body.OwnerId.Should().Be(owner.Id);
        body.OwnerUsername.Should().Be("adminep-get-owner");
    }

    [Fact]
    public async Task GetInstance_WithNonAdminToken_Returns403()
    {
        // Arrange
        using var client = CreateUserClient();

        // Act — auth check happens before handler executes
        var response = await client.GetAsync("/api/v1/admin/instances/123456789");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetInstance_WithAdminToken_Returns404ForUnknownId()
    {
        // Arrange
        using var client = CreateAdminClient();

        // Act — use an ID that doesn't exist
        var response = await client.GetAsync("/api/v1/admin/instances/99999999999999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DTOs for deserialization ───────────────────────────────────────────────

    private sealed record AdminListInstancesResponseDto(
        List<AdminInstanceListItemDto> Instances,
        int Total,
        int Page,
        int PageSize
    );

    private sealed record AdminInstanceListItemDto(
        long Id,
        string Subdomain,
        string DisplayName,
        string Status,
        string Tier,
        DateTimeOffset CreatedAt,
        string OwnerUsername
    );

    private sealed record AdminGetInstanceResponseDto(
        long Id,
        string Subdomain,
        string DisplayName,
        string Domain,
        string Status,
        string Tier,
        DateTimeOffset CreatedAt,
        DateTimeOffset? SuspendedAt,
        DateTimeOffset? DestroyedAt,
        long OwnerId,
        string OwnerUsername,
        object? ResourceLimits,
        object? FeatureFlags,
        object? Health,
        object? Infrastructure
    );
}
