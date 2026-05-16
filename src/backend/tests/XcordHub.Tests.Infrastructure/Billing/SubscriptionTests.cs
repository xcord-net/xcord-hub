using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using XcordHub.Entities;
using XcordHub.Features.Billing;
using XcordHub.Features.Instances;
using XcordHub.Tests.Infrastructure.Fixtures;

namespace XcordHub.Tests.Infrastructure.Billing;

/// <summary>
/// Tests for CancelInstanceBillingHandler -- subscription cancel / downgrade flows.
/// </summary>
[Collection("SharedPostgres")]
[Trait("Category", "Integration")]
public sealed class SubscriptionTests : BillingTestsBase
{
    public SubscriptionTests(SharedPostgresFixture fixture) : base(fixture, "xcordhub_billing_sub_test") { }

    [Fact]
    public async Task CancelBilling_PaidInstance_DowngradesToFreeTier()
    {
        await using var dbContext = CreateDbContext();
        var (user, instanceId) = await SeedInstanceAsync(dbContext, UserIdBase + 20, "cancel_1",
            InstanceTier.Basic, mediaEnabled: true);

        var handler = new CancelInstanceBillingHandler(
            dbContext,
            StubUser(user.Id),
            NoStripeOptions(),
            new NoOpStripeService(),
            NullLogger<CancelInstanceBillingHandler>.Instance);

        var result = await handler.Handle(
            new CancelInstanceBillingCommand(instanceId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Tier.Should().Be("Free");

        await using var verifyCtx = CreateDbContext();
        var billing = await verifyCtx.InstanceBillings
            .FirstOrDefaultAsync(b => b.ManagedInstanceId == instanceId);

        billing.Should().NotBeNull();
        billing!.Tier.Should().Be(InstanceTier.Free);
        billing.MediaEnabled.Should().BeFalse();
        billing.BillingStatus.Should().Be(BillingStatus.Cancelled);
        billing.StripeSubscriptionId.Should().BeNull();
        billing.StripePriceId.Should().BeNull();
        billing.CurrentPeriodEnd.Should().BeNull();
        billing.NextBillingDate.Should().BeNull();
    }

    [Fact]
    public async Task CancelBilling_WithStripeSubscription_CallsCancelSubscription()
    {
        await using var dbContext = CreateDbContext();
        var (user, instanceId) = await SeedInstanceAsync(dbContext, UserIdBase + 21, "cancel_stripe",
            InstanceTier.Pro, mediaEnabled: true);

        // Inject a fake Stripe subscription ID into the billing record
        var billing = await dbContext.InstanceBillings
            .FirstAsync(b => b.ManagedInstanceId == instanceId);
        billing.StripeSubscriptionId = "sub_test_fake_12345";
        await dbContext.SaveChangesAsync();

        var stripeStub = new SpyStripeService("https://checkout.stripe.com/fake");

        var handler = new CancelInstanceBillingHandler(
            dbContext,
            StubUser(user.Id),
            FakeStripeOptions(),
            stripeStub,
            NullLogger<CancelInstanceBillingHandler>.Instance);

        var result = await handler.Handle(
            new CancelInstanceBillingCommand(instanceId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        stripeStub.CancelSubscriptionCalled.Should().BeTrue(
            "handler must call Stripe's cancel subscription API when a subscription ID exists");
        stripeStub.LastCancelledSubscriptionId.Should().Be("sub_test_fake_12345");
    }

    [Fact]
    public async Task CancelBilling_AlreadyOnFreePlan_ReturnsBadRequest()
    {
        await using var dbContext = CreateDbContext();
        // Default seed is Free tier, mediaEnabled=false (free plan)
        var (user, instanceId) = await SeedInstanceAsync(dbContext, UserIdBase + 22, "cancel_free");

        var handler = new CancelInstanceBillingHandler(
            dbContext,
            StubUser(user.Id),
            NoStripeOptions(),
            new NoOpStripeService(),
            NullLogger<CancelInstanceBillingHandler>.Instance);

        var result = await handler.Handle(
            new CancelInstanceBillingCommand(instanceId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ALREADY_FREE");
    }

    [Fact]
    public async Task CancelBilling_NonOwner_ReturnsForbidden()
    {
        await using var dbContext = CreateDbContext();
        var (_, instanceId) = await SeedInstanceAsync(dbContext, UserIdBase + 23, "cancel_forbid",
            InstanceTier.Basic, mediaEnabled: true);

        var differentUserId = UserIdBase + 91;
        var handler = new CancelInstanceBillingHandler(
            dbContext,
            StubUser(differentUserId),
            NoStripeOptions(),
            new NoOpStripeService(),
            NullLogger<CancelInstanceBillingHandler>.Instance);

        var result = await handler.Handle(
            new CancelInstanceBillingCommand(instanceId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("NOT_OWNER");
    }

    [Fact]
    public async Task CancelBilling_ConfigDowngradedToFreeLimits()
    {
        await using var dbContext = CreateDbContext();
        var (user, instanceId) = await SeedInstanceAsync(dbContext, UserIdBase + 24, "cancel_config",
            InstanceTier.Enterprise, mediaEnabled: true);

        var handler = new CancelInstanceBillingHandler(
            dbContext,
            StubUser(user.Id),
            NoStripeOptions(),
            new NoOpStripeService(),
            NullLogger<CancelInstanceBillingHandler>.Instance);

        await handler.Handle(new CancelInstanceBillingCommand(instanceId), CancellationToken.None);

        await using var verifyCtx = CreateDbContext();
        var config = await verifyCtx.InstanceConfigs
            .FirstOrDefaultAsync(c => c.ManagedInstanceId == instanceId);

        config.Should().NotBeNull();
        var limits = JsonSerializer.Deserialize<ResourceLimits>(config!.ResourceLimitsJson);
        limits!.MaxUsers.Should().Be(10,
            "cancellation must reset resource limits to the free tier maximums");

        var flags = JsonSerializer.Deserialize<FeatureFlags>(config.FeatureFlagsJson);
        flags!.CanUseVoiceChannels.Should().BeFalse(
            "cancellation must disable voice channels (Free tier, mediaEnabled=false)");
        flags.CanUseVideoChannels.Should().BeFalse(
            "cancellation must disable video channels (Free tier, mediaEnabled=false)");
    }
}
