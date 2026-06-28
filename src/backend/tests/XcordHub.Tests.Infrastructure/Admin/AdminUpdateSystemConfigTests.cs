using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Infrastructure.Fixtures;
using Xunit;

namespace XcordHub.Tests.Infrastructure.Admin;

/// <summary>
/// Integration tests for the AdminUpdateSystemConfigHandler endpoint logic via
/// the underlying ISystemConfigService. The handler itself is a thin route
/// mapper -- its work is delegated to SystemConfigService.SetPaidServersDisabledAsync,
/// which is the contract we verify here.
///
/// Database name is unique to this test class to avoid the SystemConfig
/// singleton row being mutated by other tests.
/// </summary>
[Collection("SharedPostgres")]
[Trait("Category", "Integration")]
public sealed class AdminUpdateSystemConfigTests : IAsyncLifetime
{
    private const string TestEncryptionKey = "admin-update-syscfg-tests-encryption-key-256-bits-req!!";

    private readonly SharedPostgresFixture _fixture;
    private string _connectionString = string.Empty;

    public AdminUpdateSystemConfigTests(SharedPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture
            .CreateDatabaseAsync("xcordhub_admin_syscfg_test", TestEncryptionKey);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HubDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
    }

    [Fact]
    public async Task SetPaidServersDisabled_FirstCall_CreatesSingletonAndPersistsValue()
    {
        await using var db = CreateDbContext();
        var service = new SystemConfigService(db);

        var result = await service.SetPaidServersDisabledAsync(true, CancellationToken.None);

        result.PaidServersDisabled.Should().BeTrue(
            "the service must return the freshly-mutated config to the caller");
        result.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10),
            "UpdatedAt must reflect the time of the change");

        // Verify the row was actually persisted, not just returned in-memory.
        await using var verifyDb = CreateDbContext();
        var persisted = await verifyDb.SystemConfigs
            .FirstOrDefaultAsync(c => c.Id == SystemConfig.SingletonId);
        persisted.Should().NotBeNull(
            "calling SetPaidServersDisabled must persist a singleton SystemConfig row");
        persisted!.PaidServersDisabled.Should().BeTrue();
    }

    [Fact]
    public async Task SetPaidServersDisabled_Toggle_FlipsValueAndAdvancesUpdatedAt()
    {
        await using var db = CreateDbContext();
        var service = new SystemConfigService(db);

        // First call - enable the flag (creates the singleton row if absent).
        var enabled = await service.SetPaidServersDisabledAsync(true, CancellationToken.None);
        var enabledAt = enabled.UpdatedAt;

        // Second call - flip it back. The handler must mutate the same row,
        // not create a second SystemConfig.
        var disabled = await service.SetPaidServersDisabledAsync(false, CancellationToken.None);

        disabled.PaidServersDisabled.Should().BeFalse(
            "toggling must reflect the new value");
        disabled.UpdatedAt.Should().BeOnOrAfter(enabledAt,
            "UpdatedAt must monotonically advance across mutations");

        await using var verifyDb = CreateDbContext();
        var count = await verifyDb.SystemConfigs.CountAsync();
        count.Should().Be(1,
            "SystemConfig is a singleton - repeated writes must reuse SingletonId");
    }

    [Fact]
    public async Task GetAsync_NoPriorWrite_CreatesDefaultRowWithPaidServersEnabled()
    {
        await using var db = CreateDbContext();
        var service = new SystemConfigService(db);

        // Reset any prior state - test isolation is per-class, but if this test
        // happened to run first the row should not exist.
        var existing = await db.SystemConfigs.FirstOrDefaultAsync();
        if (existing != null)
        {
            db.SystemConfigs.Remove(existing);
            await db.SaveChangesAsync();
        }

        var config = await service.GetAsync(CancellationToken.None);

        config.Should().NotBeNull();
        config.Id.Should().Be(SystemConfig.SingletonId,
            "GetAsync must materialize the singleton row on first read");
        config.PaidServersDisabled.Should().BeFalse(
            "paid servers default to enabled (PaidServersDisabled = false)");
    }
}
