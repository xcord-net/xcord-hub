using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Tests.Infrastructure;

/// <summary>
/// HTTP integration tests for the GET /api/v1/admin/health endpoint, which is served by
/// GetAggregatedHealthHandler (migrated from the legacy GetAggregatedHealthEndpoint static class).
/// Verifies authorization enforcement and response schema correctness.
/// Uses the shared AdminEndpointFixture (Testcontainers PG + Redis + WebApplicationFactory).
/// </summary>
[Collection("AdminEndpoint")]
[Trait("Category", "Integration")]
public sealed class AdminHealthEndpointTests
{
    private readonly AdminEndpointFixture _fixture;

    public AdminHealthEndpointTests(AdminEndpointFixture fixture)
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
        var token = jwtService.GenerateAccessToken(3_000_000_001L, isAdmin: true);
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
        var token = jwtService.GenerateAccessToken(3_000_000_002L, isAdmin: false);
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
        Email = Encoding.UTF8.GetBytes($"encrypted-{username}@test.com"),
        EmailHash = Encoding.UTF8.GetBytes($"hash-{username}"),
        PasswordHash = "hashed",
        IsAdmin = false,
        IsDisabled = false,
        CreatedAt = DateTimeOffset.UtcNow,
        LastLoginAt = DateTimeOffset.UtcNow
    };

    // ── GET /api/v1/admin/health — authorization ─────────────────────────────

    [Fact]
    public async Task GetHealth_WithAdminToken_Returns200()
    {
        using var client = CreateAdminClient();

        var response = await client.GetAsync("/api/v1/admin/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealth_WithNonAdminToken_Returns403()
    {
        using var client = CreateUserClient();

        var response = await client.GetAsync("/api/v1/admin/health");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetHealth_WithNoToken_Returns401()
    {
        using var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/admin/health");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── GET /api/v1/admin/health — response schema ───────────────────────────

    [Fact]
    public async Task GetHealth_ReturnsCorrectResponseSchema()
    {
        using var client = CreateAdminClient();

        var response = await client.GetAsync("/api/v1/admin/health");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AggregatedHealthResponseDto>();

        body.Should().NotBeNull();
        body!.OverallStatus.Should().BeOneOf("Healthy", "Degraded", "Unhealthy");
        body.TotalInstances.Should().BeGreaterThanOrEqualTo(0);
        body.HealthyInstances.Should().BeGreaterThanOrEqualTo(0);
        body.UnhealthyInstances.Should().BeGreaterThanOrEqualTo(0);
        body.Instances.Should().NotBeNull();
        body.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task GetHealth_WithHealthyRunningInstance_InstanceAppearsInResponse()
    {
        await using var dbContext = CreateDbContext();

        var owner = MakeOwner(9_500_000_001L, "healthep-owner");
        dbContext.HubUsers.Add(owner);

        var instance = new ManagedInstance
        {
            Id = 9_500_000_002L,
            OwnerId = owner.Id,
            Domain = "healthep.xcord.net",
            DisplayName = "Health Endpoint Test Instance",
            Status = InstanceStatus.Running,
            SnowflakeWorkerId = 950,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ManagedInstances.Add(instance);

        dbContext.InstanceHealths.Add(new InstanceHealth
        {
            Id = 9_500_000_003L,
            ManagedInstanceId = instance.Id,
            IsHealthy = true,
            ConsecutiveFailures = 0,
            ResponseTimeMs = 55,
            LastCheckAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();

        using var client = CreateAdminClient();

        var response = await client.GetAsync("/api/v1/admin/health");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AggregatedHealthResponseDto>();

        body.Should().NotBeNull();
        var dto = body!.Instances.FirstOrDefault(i => i.Id == instance.Id);
        dto.Should().NotBeNull("the seeded instance should appear in the health response");
        dto!.IsHealthy.Should().BeTrue();
        dto.Domain.Should().Be("healthep.xcord.net");
        dto.Status.Should().Be("Running");
        dto.ResponseTimeMs.Should().Be(55);
    }

    [Fact]
    public async Task GetHealth_WithUnhealthyInstance_OverallStatusIsDegradedOrUnhealthy()
    {
        await using var dbContext = CreateDbContext();

        var owner = MakeOwner(9_500_000_010L, "unhealthyep-owner");
        dbContext.HubUsers.Add(owner);

        var instance = new ManagedInstance
        {
            Id = 9_500_000_011L,
            OwnerId = owner.Id,
            Domain = "unhealthyep.xcord.net",
            DisplayName = "Unhealthy Endpoint Test Instance",
            Status = InstanceStatus.Running,
            SnowflakeWorkerId = 951,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ManagedInstances.Add(instance);

        dbContext.InstanceHealths.Add(new InstanceHealth
        {
            Id = 9_500_000_012L,
            ManagedInstanceId = instance.Id,
            IsHealthy = false,
            ConsecutiveFailures = 3,
            ErrorMessage = "Connection timed out",
            LastCheckAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();

        using var client = CreateAdminClient();

        var response = await client.GetAsync("/api/v1/admin/health");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AggregatedHealthResponseDto>();

        body.Should().NotBeNull();
        var dto = body!.Instances.FirstOrDefault(i => i.Id == instance.Id);
        dto.Should().NotBeNull();
        dto!.IsHealthy.Should().BeFalse();
        dto.ErrorMessage.Should().Be("Connection timed out");
        dto.ConsecutiveFailures.Should().Be(3);
        body.OverallStatus.Should().BeOneOf("Unhealthy", "Degraded");
    }

    [Fact]
    public async Task GetHealth_ExcludesSuspendedInstances()
    {
        await using var dbContext = CreateDbContext();

        var owner = MakeOwner(9_500_000_020L, "suspendedep-owner");
        dbContext.HubUsers.Add(owner);

        var suspendedInstance = new ManagedInstance
        {
            Id = 9_500_000_021L,
            OwnerId = owner.Id,
            Domain = "suspendedep.xcord.net",
            DisplayName = "Suspended Endpoint Test Instance",
            Status = InstanceStatus.Suspended,
            SnowflakeWorkerId = 952,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ManagedInstances.Add(suspendedInstance);
        await dbContext.SaveChangesAsync();

        using var client = CreateAdminClient();

        var response = await client.GetAsync("/api/v1/admin/health");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AggregatedHealthResponseDto>();

        body.Should().NotBeNull();
        body!.Instances.Should().NotContain(
            i => i.Id == suspendedInstance.Id,
            "suspended instances should be excluded from health aggregation");
    }

    // ── Private DTOs for deserialization ──────────────────────────────────────

    private sealed record AggregatedHealthResponseDto(
        string OverallStatus,
        int TotalInstances,
        int HealthyInstances,
        int UnhealthyInstances,
        DateTimeOffset Timestamp,
        List<InstanceHealthDtoRecord> Instances
    );

    private sealed record InstanceHealthDtoRecord(
        long Id,
        string Domain,
        string Status,
        bool IsHealthy,
        int ConsecutiveFailures,
        int? ResponseTimeMs,
        string? ErrorMessage,
        DateTimeOffset? LastCheckAt
    );
}
