using FluentAssertions;
using XcordHub.Entities;
using XcordHub.Features.Billing;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Infrastructure.Fixtures;

namespace XcordHub.Tests.Infrastructure.Billing;

/// <summary>
/// Tests for GetBillingHandler -- listing instance billing records for a user.
/// </summary>
[Collection("SharedPostgres")]
[Trait("Category", "Integration")]
public sealed class GetBillingTests : BillingTestsBase
{
    public GetBillingTests(SharedPostgresFixture fixture) : base(fixture, "xcordhub_billing_get_test") { }

    [Fact]
    public async Task GetBilling_UserWithInstance_ReturnsInstanceBillingItem()
    {
        await using var dbContext = CreateDbContext();
        var (user, instanceId) = await SeedInstanceAsync(dbContext, UserIdBase + 1, "get_billing_1",
            InstanceTier.Basic, mediaEnabled: true);

        var handler = new GetBillingHandler(dbContext, StubUser(user.Id));
        var result = await handler.Handle(new GetBillingQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Instances.Should().HaveCount(1);

        var item = result.Value.Instances[0];
        item.InstanceId.Should().Be(instanceId.ToString());
        item.Tier.Should().Be("Basic");
        item.MediaEnabled.Should().BeTrue();
        item.PriceCents.Should().Be(TierDefaults.GetTotalPriceCents(InstanceTier.Basic, mediaEnabled: true));
    }

    [Fact]
    public async Task GetBilling_UserWithNoInstances_ReturnsEmptyList()
    {
        await using var dbContext = CreateDbContext();

        // Seed a user but no instances
        var encryptionService = new AesEncryptionService(TestEncryptionKey);
        var user = new HubUser
        {
            Id = UserIdBase + 2,
            Username = "billing_empty_user",
            DisplayName = "Empty User",
            Email = encryptionService.Encrypt("empty@test.invalid"),
            EmailHash = encryptionService.ComputeHmac("empty@test.invalid"),
            PasswordHash = "hashed",
            IsAdmin = false,
            IsDisabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = DateTimeOffset.UtcNow
        };
        dbContext.HubUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var handler = new GetBillingHandler(dbContext, StubUser(user.Id));
        var result = await handler.Handle(new GetBillingQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Instances.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBilling_UnknownUser_ReturnsNotFound()
    {
        await using var dbContext = CreateDbContext();

        var handler = new GetBillingHandler(dbContext, StubUser(999_000_000_000L));
        var result = await handler.Handle(new GetBillingQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("USER_NOT_FOUND");
    }
}
