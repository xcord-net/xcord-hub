using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using XcordHub.Entities;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Infrastructure.Fixtures;

namespace XcordHub.Tests.Infrastructure.Instances;

/// <summary>
/// Integration tests for CheckSubdomainHandler logic. The existing CheckSubdomainTests
/// at the root covers HTTP auth only; this class covers handler return shape:
/// available subdomain, taken subdomain, reserved subdomain, and validation failures.
///
/// Owner IDs: 1_313_000_000 – 1_313_000_099
/// Instance IDs: 2_313_000_000 – 2_313_000_099
/// </summary>
[Collection("SharedPostgres")]
[Trait("Category", "Integration")]
public sealed class CheckSubdomainHandlerTests
{
    private const string TestEncryptionKey = "check-subdomain-handler-tests-encryption-key-256-req!!";
    private const long UserIdBase = 1_313_000_000L;
    private const long InstanceIdBase = 2_313_000_000L;

    private readonly string _connectionString;

    public CheckSubdomainHandlerTests(SharedPostgresFixture fixture)
    {
        _connectionString = fixture
            .CreateDatabaseAsync("xcordhub_check_subdomain_handler_test", TestEncryptionKey)
            .GetAwaiter().GetResult();
    }

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

    [Fact]
    public async Task CheckSubdomain_AvailableSubdomain_ReturnsAvailableTrue()
    {
        await using var db = CreateDbContext();
        var handler = new CheckSubdomainHandler(db, BuildConfiguration());

        var result = await handler.Handle(
            new CheckSubdomainQuery("freshname"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Available.Should().BeTrue(
            "an unused well-formed subdomain must be reported as available");
        result.Value.Reason.Should().BeNull("Reason must be null when Available is true");
    }

    [Fact]
    public async Task CheckSubdomain_TakenSubdomain_ReturnsAvailableFalse()
    {
        await using var db = CreateDbContext();
        var enc = new AesEncryptionService(TestEncryptionKey);
        var owner = new HubUser
        {
            Id = UserIdBase + 1,
            Username = "csh_owner",
            DisplayName = "CSH Owner",
            Email = enc.Encrypt("csh_owner@test.invalid"),
            EmailHash = enc.ComputeHmac("csh_owner@test.invalid"),
            PasswordHash = "hashed",
            CreatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = DateTimeOffset.UtcNow
        };
        var instance = new ManagedInstance
        {
            Id = InstanceIdBase + 1,
            OwnerId = owner.Id,
            Domain = "takenname.xcord-dev.net",
            DisplayName = "Taken Name",
            Status = InstanceStatus.Running,
            SnowflakeWorkerId = 313,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.HubUsers.Add(owner);
        db.ManagedInstances.Add(instance);
        await db.SaveChangesAsync();

        var handler = new CheckSubdomainHandler(db, BuildConfiguration());
        var result = await handler.Handle(
            new CheckSubdomainQuery("takenname"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Available.Should().BeFalse(
            "an existing Domain row with the same subdomain must mark it unavailable");
        result.Value.Reason.Should().Contain("taken",
            "the Reason should indicate the conflict");
    }

    [Fact]
    public async Task CheckSubdomain_ReservedSubdomain_ReturnsAvailableFalse()
    {
        await using var db = CreateDbContext();
        var handler = new CheckSubdomainHandler(db, BuildConfiguration());

        // "admin" is in ValidationHelpers.ReservedSubdomains.
        var result = await handler.Handle(
            new CheckSubdomainQuery("admin"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(
            "validation failures surface as Available=false, not as a Result failure");
        result.Value.Available.Should().BeFalse();
        result.Value.Reason.Should().NotBeNullOrEmpty(
            "the Reason should explain why the subdomain is unavailable");
    }

    [Fact]
    public void CheckSubdomain_EmptySubdomain_ValidateRejects()
    {
        using var db = CreateDbContext();
        var handler = new CheckSubdomainHandler(db, BuildConfiguration());

        var error = handler.Validate(new CheckSubdomainQuery(""));

        error.Should().NotBeNull();
        error!.Code.Should().Be("VALIDATION_FAILED",
            "an empty subdomain must be rejected at validation time");
    }
}
