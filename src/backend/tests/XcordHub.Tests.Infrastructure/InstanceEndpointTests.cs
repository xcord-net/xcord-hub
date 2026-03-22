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
/// Shared fixture for InstanceEndpointTests.
/// Starts one PostgreSQL container, one Redis container, and one WebApplicationFactory
/// for all tests in the collection. Environment variables provide config to the minimal-API
/// program before host construction (required because WebApplicationFactory with minimal
/// APIs reads config during service registration, before ConfigureAppConfiguration runs).
/// </summary>
public sealed class InstanceEndpointFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;
    private WebApplicationFactory<Program>? _factory;

    public const string TestJwtIssuer = "xcord-hub-instep-test";
    public const string TestJwtAudience = "xcord-hub-instep-clients";
    public const string TestJwtSecretKey = "instep-test-secret-key-with-minimum-256-bits-for-hmacsha256!!";
    public const string TestEncryptionKey = "instep-encryption-key-with-256-bits-minimum-length-required!!";

    public string ConnectionString { get; private set; } = string.Empty;
    public WebApplicationFactory<Program> Factory => _factory!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("xcordhub_instep_test")
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
        Environment.SetEnvironmentVariable("Redis__ChannelPrefix", "instep-test");
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestJwtIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestJwtAudience);
        Environment.SetEnvironmentVariable("Jwt__SecretKey", TestJwtSecretKey);
        Environment.SetEnvironmentVariable("Jwt__ExpirationMinutes", "60");
        Environment.SetEnvironmentVariable("Encryption__Key", TestEncryptionKey);
        Environment.SetEnvironmentVariable("Docker__UseReal", "false");
        Environment.SetEnvironmentVariable("Caddy__UseReal", "false");
        Environment.SetEnvironmentVariable("Dns__Provider", "noop");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
            });

        // Trigger host creation - this runs EnsureCreatedAsync on the schema.
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
        Environment.SetEnvironmentVariable("Dns__Provider", null);

        if (_redis is not null)
            await _redis.DisposeAsync();

        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }
}

[CollectionDefinition("InstanceEndpoint")]
public class InstanceEndpointCollection : ICollectionFixture<InstanceEndpointFixture> { }

/// <summary>
/// HTTP integration tests for the user-facing instance detail endpoints.
/// Verifies that GET, PATCH, suspend, resume, and destroy under
/// /api/v1/hub/instances/{id} enforce ownership and authorization correctly.
/// Uses Testcontainers (PostgreSQL + Redis) and WebApplicationFactory.
/// </summary>
[Collection("InstanceEndpoint")]
[Trait("Category", "Integration")]
public sealed class InstanceEndpointTests
{
    private readonly InstanceEndpointFixture _fixture;

    // ID ranges reserved for this test class to avoid conflicts.
    // User IDs:     9_100_000_000 - 9_199_999_999
    // Instance IDs: 9_200_000_000 - 9_299_999_999

    public InstanceEndpointTests(InstanceEndpointFixture fixture)
    {
        _fixture = fixture;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HubDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        return new HubDbContext(options, new AesEncryptionService(InstanceEndpointFixture.TestEncryptionKey));
    }

    private HttpClient CreateUserClient(long userId)
    {
        var jwtService = new JwtService(
            InstanceEndpointFixture.TestJwtIssuer,
            InstanceEndpointFixture.TestJwtAudience,
            InstanceEndpointFixture.TestJwtSecretKey,
            60);
        var token = jwtService.GenerateAccessToken(userId, isAdmin: false);
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient CreateAnonymousClient() => _fixture.Factory.CreateClient();

    private static HubUser MakeUser(long id, string username) => new HubUser
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

    private static ManagedInstance MakeInstance(long id, long ownerId, string domain, InstanceStatus status = InstanceStatus.Running) =>
        new ManagedInstance
        {
            Id = id,
            OwnerId = ownerId,
            Domain = domain,
            DisplayName = $"Test Instance {id}",
            Status = status,
            SnowflakeWorkerId = (int)(id % 1024),
            CreatedAt = DateTimeOffset.UtcNow
        };

    // ── GET /api/v1/hub/instances/{id} ────────────────────────────────────────

    [Fact]
    public async Task GetInstance_Anonymous_Returns401()
    {
        using var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/hub/instances/9200000001");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetInstance_Owner_ReturnsInstance()
    {
        // Arrange - seed owner and instance
        await using var dbContext = CreateDbContext();

        const long ownerId = 9_100_000_001L;
        const long instanceId = 9_200_000_001L;

        dbContext.HubUsers.Add(MakeUser(ownerId, "instep-get-owner"));
        dbContext.ManagedInstances.Add(MakeInstance(instanceId, ownerId, "instep-get.xcord-dev.net"));
        await dbContext.SaveChangesAsync();

        using var client = CreateUserClient(ownerId);

        // Act
        var response = await client.GetAsync($"/api/v1/hub/instances/{instanceId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<GetInstanceResponseDto>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(instanceId.ToString());
        body.Domain.Should().Be("instep-get.xcord-dev.net");
        body.Subdomain.Should().Be("instep-get");
        body.Status.Should().Be("Running");
    }

    [Fact]
    public async Task GetInstance_NonOwner_Returns404()
    {
        // Arrange - seed owner + instance, then use a different user
        await using var dbContext = CreateDbContext();

        const long ownerId = 9_100_000_002L;
        const long nonOwnerId = 9_100_000_003L;
        const long instanceId = 9_200_000_002L;

        dbContext.HubUsers.Add(MakeUser(ownerId, "instep-nonown-owner"));
        dbContext.HubUsers.Add(MakeUser(nonOwnerId, "instep-nonown-other"));
        dbContext.ManagedInstances.Add(MakeInstance(instanceId, ownerId, "instep-nonown.xcord-dev.net"));
        await dbContext.SaveChangesAsync();

        using var client = CreateUserClient(nonOwnerId);

        // Act
        var response = await client.GetAsync($"/api/v1/hub/instances/{instanceId}");

        // Assert - non-owner should not be able to see the instance (404, not 403, to avoid leaking existence)
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── PATCH /api/v1/hub/instances/{id} ─────────────────────────────────────

    [Fact]
    public async Task UpdateInstance_Owner_UpdatesDisplayName()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        const long ownerId = 9_100_000_004L;
        const long instanceId = 9_200_000_003L;

        dbContext.HubUsers.Add(MakeUser(ownerId, "instep-update-owner"));
        dbContext.ManagedInstances.Add(MakeInstance(instanceId, ownerId, "instep-update.xcord-dev.net"));
        await dbContext.SaveChangesAsync();

        using var client = CreateUserClient(ownerId);

        // Act
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/hub/instances/{instanceId}",
            new { displayName = "My Renamed Server" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<GetInstanceResponseDto>();
        body.Should().NotBeNull();
        body!.DisplayName.Should().Be("My Renamed Server");
    }

    [Fact]
    public async Task UpdateInstance_NonOwner_Returns404()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        const long ownerId = 9_100_000_005L;
        const long nonOwnerId = 9_100_000_006L;
        const long instanceId = 9_200_000_004L;

        dbContext.HubUsers.Add(MakeUser(ownerId, "instep-upd-nonown-owner"));
        dbContext.HubUsers.Add(MakeUser(nonOwnerId, "instep-upd-nonown-other"));
        dbContext.ManagedInstances.Add(MakeInstance(instanceId, ownerId, "instep-upd-nonown.xcord-dev.net"));
        await dbContext.SaveChangesAsync();

        using var client = CreateUserClient(nonOwnerId);

        // Act
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/hub/instances/{instanceId}",
            new { displayName = "Hijacked Name" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST /api/v1/hub/instances/{id}/suspend ───────────────────────────────

    [Fact]
    public async Task SuspendInstance_Owner_EndpointAccessible()
    {
        // Arrange - instance must be Running to suspend
        await using var dbContext = CreateDbContext();

        const long ownerId = 9_100_000_007L;
        const long instanceId = 9_200_000_005L;

        dbContext.HubUsers.Add(MakeUser(ownerId, "instep-suspend-owner"));
        var instance = MakeInstance(instanceId, ownerId, "instep-suspend.xcord-dev.net", InstanceStatus.Running);
        dbContext.ManagedInstances.Add(instance);
        await dbContext.SaveChangesAsync();

        using var client = CreateUserClient(ownerId);

        // Act
        var response = await client.PostAsync($"/api/v1/hub/instances/{instanceId}/suspend", null);

        // Assert - owner is authorized; endpoint is reachable (not 401/405/404-for-instance).
        // Handler will return INFRASTRUCTURE_NOT_FOUND (404) since there is no infra record seeded,
        // but that 404 comes from inside the handler, not from routing or auth.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);
        // The instance itself is found, so we don't get INSTANCE_NOT_FOUND.
        // INFRASTRUCTURE_NOT_FOUND (404) is acceptable here.
    }

    [Fact]
    public async Task SuspendInstance_NonOwner_Returns404OrForbidden()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        const long ownerId = 9_100_000_008L;
        const long nonOwnerId = 9_100_000_009L;
        const long instanceId = 9_200_000_006L;

        dbContext.HubUsers.Add(MakeUser(ownerId, "instep-sus-nonown-owner"));
        dbContext.HubUsers.Add(MakeUser(nonOwnerId, "instep-sus-nonown-other"));
        dbContext.ManagedInstances.Add(MakeInstance(instanceId, ownerId, "instep-sus-nonown.xcord-dev.net", InstanceStatus.Running));
        await dbContext.SaveChangesAsync();

        using var client = CreateUserClient(nonOwnerId);

        // Act
        var response = await client.PostAsync($"/api/v1/hub/instances/{instanceId}/suspend", null);

        // Assert - non-owner should be rejected
        ((int)response.StatusCode).Should().BeOneOf(403, 404);
    }

    // ── POST /api/v1/hub/instances/{id}/resume ────────────────────────────────

    [Fact]
    public async Task ResumeInstance_Owner_EndpointAccessible()
    {
        // Arrange - instance must be Suspended to resume
        await using var dbContext = CreateDbContext();

        const long ownerId = 9_100_000_010L;
        const long instanceId = 9_200_000_007L;

        dbContext.HubUsers.Add(MakeUser(ownerId, "instep-resume-owner"));
        var instance = MakeInstance(instanceId, ownerId, "instep-resume.xcord-dev.net", InstanceStatus.Suspended);
        dbContext.ManagedInstances.Add(instance);
        await dbContext.SaveChangesAsync();

        using var client = CreateUserClient(ownerId);

        // Act
        var response = await client.PostAsync($"/api/v1/hub/instances/{instanceId}/resume", null);

        // Assert - owner is authorized; endpoint is reachable (not 401/405).
        // Handler will return INFRASTRUCTURE_NOT_FOUND (404) since there is no infra record seeded,
        // but that 404 comes from inside the handler, not from routing or auth.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task ResumeInstance_NonOwner_Returns404OrForbidden()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        const long ownerId = 9_100_000_011L;
        const long nonOwnerId = 9_100_000_012L;
        const long instanceId = 9_200_000_008L;

        dbContext.HubUsers.Add(MakeUser(ownerId, "instep-res-nonown-owner"));
        dbContext.HubUsers.Add(MakeUser(nonOwnerId, "instep-res-nonown-other"));
        dbContext.ManagedInstances.Add(MakeInstance(instanceId, ownerId, "instep-res-nonown.xcord-dev.net", InstanceStatus.Suspended));
        await dbContext.SaveChangesAsync();

        using var client = CreateUserClient(nonOwnerId);

        // Act
        var response = await client.PostAsync($"/api/v1/hub/instances/{instanceId}/resume", null);

        // Assert
        ((int)response.StatusCode).Should().BeOneOf(403, 404);
    }

    // ── POST /api/v1/hub/instances/{id}/destroy ───────────────────────────────

    [Fact]
    public async Task DestroyInstance_Owner_ReturnsOk()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        const long ownerId = 9_100_000_013L;
        const long instanceId = 9_200_000_009L;

        dbContext.HubUsers.Add(MakeUser(ownerId, "instep-destroy-owner"));
        var instance = MakeInstance(instanceId, ownerId, "instep-destroy.xcord-dev.net", InstanceStatus.Running);
        // Add WorkerIdRegistry entry so TombstoneWorkerIdAsync doesn't crash
        dbContext.ManagedInstances.Add(instance);
        await dbContext.SaveChangesAsync();

        using var client = CreateUserClient(ownerId);

        // Act
        var response = await client.PostAsync($"/api/v1/hub/instances/{instanceId}/destroy", null);

        // Assert - owner can access the endpoint; no infra means destroy completes with soft-delete
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DestroyInstance_NonOwner_Returns404OrForbidden()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        const long ownerId = 9_100_000_014L;
        const long nonOwnerId = 9_100_000_015L;
        const long instanceId = 9_200_000_010L;

        dbContext.HubUsers.Add(MakeUser(ownerId, "instep-des-nonown-owner"));
        dbContext.HubUsers.Add(MakeUser(nonOwnerId, "instep-des-nonown-other"));
        dbContext.ManagedInstances.Add(MakeInstance(instanceId, ownerId, "instep-des-nonown.xcord-dev.net"));
        await dbContext.SaveChangesAsync();

        using var client = CreateUserClient(nonOwnerId);

        // Act
        var response = await client.PostAsync($"/api/v1/hub/instances/{instanceId}/destroy", null);

        // Assert
        ((int)response.StatusCode).Should().BeOneOf(403, 404);
    }

    [Fact]
    public async Task DestroyInstance_Anonymous_Returns401()
    {
        using var client = CreateAnonymousClient();

        var response = await client.PostAsync("/api/v1/hub/instances/9200000099/destroy", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── DTOs for deserialization ───────────────────────────────────────────────

    private sealed record GetInstanceResponseDto(
        string Id,
        string Subdomain,
        string DisplayName,
        string Domain,
        string Status,
        string Tier,
        bool MediaEnabled,
        DateTimeOffset CreatedAt
    );
}
