using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Features.Billing;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Infrastructure.Fixtures;

namespace XcordHub.Tests.Infrastructure.Billing;

/// <summary>
/// Tests for ChangePlanHandler -- moving an existing instance between tiers
/// and toggling media. Covers the no-Stripe path (plan applied directly) and
/// the Stripe path (checkout session creation).
/// </summary>
[Collection("SharedPostgres")]
[Trait("Category", "Integration")]
public sealed class CheckoutTests : BillingTestsBase
{
    public CheckoutTests(SharedPostgresFixture fixture) : base(fixture, "xcordhub_billing_checkout_test") { }

    [Fact]
    public async Task ChangePlan_NoStripe_UpgradesToBasicWithMedia_UpdatesBillingAndConfig()
    {
        await using var dbContext = CreateDbContext();
        var (user, instanceId) = await SeedInstanceAsync(dbContext, UserIdBase + 10, "change_plan_1");

        var handler = new ChangePlanHandler(
            dbContext,
            StubUser(user.Id),
            NoStripeOptions(),
            new NoOpStripeService(),
            new AesEncryptionService(TestEncryptionKey),
            BuildConfiguration());

        var command = new ChangePlanCommand(instanceId, InstanceTier.Basic, MediaEnabled: true);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RequiresCheckout.Should().BeFalse(
            "no Stripe configured, plan change should be applied directly");
        result.Value.CheckoutUrl.Should().BeNull();
        result.Value.Tier.Should().Be("Basic");
        result.Value.PriceCents.Should().Be(TierDefaults.GetTotalPriceCents(InstanceTier.Basic, mediaEnabled: true));

        await using var verifyCtx = CreateDbContext();
        var billing = await verifyCtx.InstanceBillings
            .FirstOrDefaultAsync(b => b.ManagedInstanceId == instanceId);

        billing.Should().NotBeNull();
        billing!.Tier.Should().Be(InstanceTier.Basic,
            "billing record must reflect the upgraded tier");
        billing.MediaEnabled.Should().BeTrue(
            "billing record must reflect the upgraded media enabled flag");

        var config = await verifyCtx.InstanceConfigs
            .FirstOrDefaultAsync(c => c.ManagedInstanceId == instanceId);

        config.Should().NotBeNull();
        var flags = JsonSerializer.Deserialize<FeatureFlags>(config!.FeatureFlagsJson);
        flags!.CanUseVoiceChannels.Should().BeTrue(
            "mediaEnabled should enable voice channels in feature flags");
        flags.CanUseVideoChannels.Should().BeTrue(
            "mediaEnabled should enable video channels in feature flags");
    }

    [Fact]
    public async Task ChangePlan_NoStripe_UpgradesToProWithMedia_SetsFlagsCorrectly()
    {
        await using var dbContext = CreateDbContext();
        var (user, instanceId) = await SeedInstanceAsync(dbContext, UserIdBase + 11, "change_plan_hd");

        var handler = new ChangePlanHandler(
            dbContext,
            StubUser(user.Id),
            NoStripeOptions(),
            new NoOpStripeService(),
            new AesEncryptionService(TestEncryptionKey),
            BuildConfiguration());

        var command = new ChangePlanCommand(instanceId, InstanceTier.Pro, MediaEnabled: true);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Tier.Should().Be("Pro");

        await using var verifyCtx = CreateDbContext();
        var billing = await verifyCtx.InstanceBillings
            .FirstOrDefaultAsync(b => b.ManagedInstanceId == instanceId);

        billing!.Tier.Should().Be(InstanceTier.Pro);
        billing.MediaEnabled.Should().BeTrue();

        var config = await verifyCtx.InstanceConfigs
            .FirstOrDefaultAsync(c => c.ManagedInstanceId == instanceId);

        var flags = JsonSerializer.Deserialize<FeatureFlags>(config!.FeatureFlagsJson);
        flags!.CanUseSimulcast.Should().BeTrue("mediaEnabled must enable simulcast in feature flags");
        flags.CanUseVoiceChannels.Should().BeTrue();
        flags.CanUseVideoChannels.Should().BeTrue();
    }

    [Fact]
    public async Task ChangePlan_WithStripe_PaidTier_ReturnsCheckoutUrl()
    {
        await using var dbContext = CreateDbContext();
        var (user, instanceId) = await SeedInstanceAsync(dbContext, UserIdBase + 12, "change_plan_stripe");

        // Stub: Stripe is "configured" but the service is a spy that captures the call
        var stripeStub = new SpyStripeService("https://checkout.stripe.com/fake-session-url");

        var handler = new ChangePlanHandler(
            dbContext,
            StubUser(user.Id),
            FakeStripeOptions(),
            stripeStub,
            new AesEncryptionService(TestEncryptionKey),
            BuildConfiguration());

        var command = new ChangePlanCommand(instanceId, InstanceTier.Basic, MediaEnabled: true);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RequiresCheckout.Should().BeTrue(
            "Stripe is configured and the plan is paid, so a checkout session must be created");
        result.Value.CheckoutUrl.Should().Be("https://checkout.stripe.com/fake-session-url");

        stripeStub.EnsureCustomerCalled.Should().BeTrue(
            "handler must ensure a Stripe customer exists before creating a checkout session");
        stripeStub.CreateCheckoutCalled.Should().BeTrue(
            "handler must call CreateCheckoutSessionAsync when Stripe is configured");

        // Billing record should NOT be modified yet - plan only activates after webhook
        await using var verifyCtx = CreateDbContext();
        var billing = await verifyCtx.InstanceBillings
            .FirstOrDefaultAsync(b => b.ManagedInstanceId == instanceId);

        billing!.Tier.Should().Be(InstanceTier.Free,
            "billing must not change until the Stripe webhook confirms payment");
    }

    [Fact]
    public async Task ChangePlan_SamePlan_ReturnsBadRequest()
    {
        await using var dbContext = CreateDbContext();
        var (user, instanceId) = await SeedInstanceAsync(dbContext, UserIdBase + 13, "same_plan");

        var handler = new ChangePlanHandler(
            dbContext,
            StubUser(user.Id),
            NoStripeOptions(),
            new NoOpStripeService(),
            new AesEncryptionService(TestEncryptionKey),
            BuildConfiguration());

        // Instance starts on Free (the default) - try to "change" to the same plan
        var command = new ChangePlanCommand(instanceId, InstanceTier.Free, MediaEnabled: false);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("SAME_PLAN");
    }

    [Fact]
    public async Task ChangePlan_NonOwner_ReturnsForbidden()
    {
        await using var dbContext = CreateDbContext();
        var (_, instanceId) = await SeedInstanceAsync(dbContext, UserIdBase + 14, "change_plan_forbid");

        var differentUserId = UserIdBase + 90;
        var handler = new ChangePlanHandler(
            dbContext,
            StubUser(differentUserId),
            NoStripeOptions(),
            new NoOpStripeService(),
            new AesEncryptionService(TestEncryptionKey),
            BuildConfiguration());

        var command = new ChangePlanCommand(instanceId, InstanceTier.Basic, MediaEnabled: true);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("NOT_OWNER");
    }

    [Fact]
    public async Task ChangePlan_InvalidTier_ReturnsValidationError()
    {
        await using var dbContext = CreateDbContext();

        var handler = new ChangePlanHandler(
            dbContext,
            StubUser(UserIdBase + 15),
            NoStripeOptions(),
            new NoOpStripeService(),
            new AesEncryptionService(TestEncryptionKey),
            BuildConfiguration());

        // Pass an out-of-range enum value to trigger validation
        var command = new ChangePlanCommand(999L, (InstanceTier)99);
        var error = handler.Validate(command);

        error.Should().NotBeNull();
        error!.Code.Should().Be("VALIDATION_FAILED",
            "an invalid tier value must be rejected by the validator");
    }

    [Fact]
    public void ChangePlan_ToPaidTier_RejectsWithPaidTierUnavailable()
    {
        using var dbContext = CreateDbContext();

        var handler = new ChangePlanHandler(
            dbContext,
            StubUser(UserIdBase + 50),
            NoStripeOptions(),
            new NoOpStripeService(),
            new AesEncryptionService(TestEncryptionKey),
            BuildConfiguration());

        // InstanceId 999999 doesn't exist in DB - validation runs before Handle so that's fine
        var command = new ChangePlanCommand(999999L, InstanceTier.Basic);
        var error = handler.Validate(command);

        error.Should().NotBeNull();
        error!.Code.Should().Be("PAID_TIER_UNAVAILABLE",
            "beta gate must reject any paid tier upgrade attempt");
    }

    [Fact]
    public void ChangePlan_EnableMedia_RejectsWithMediaUnavailable()
    {
        using var dbContext = CreateDbContext();

        var handler = new ChangePlanHandler(
            dbContext,
            StubUser(UserIdBase + 51),
            NoStripeOptions(),
            new NoOpStripeService(),
            new AesEncryptionService(TestEncryptionKey),
            BuildConfiguration());

        var command = new ChangePlanCommand(999999L, InstanceTier.Free, MediaEnabled: true);
        var error = handler.Validate(command);

        error.Should().NotBeNull();
        error!.Code.Should().Be("MEDIA_UNAVAILABLE",
            "beta gate must reject any attempt to enable voice & video");
    }

    [Fact]
    public async Task ChangePlan_UnknownInstance_ReturnsNotFound()
    {
        await using var dbContext = CreateDbContext();

        // Seed the user so auth passes, but use a nonexistent instance ID
        var encryptionService = new AesEncryptionService(TestEncryptionKey);
        var user = new HubUser
        {
            Id = UserIdBase + 16,
            Username = "billing_noinstance",
            DisplayName = "No Instance",
            Email = encryptionService.Encrypt("noinstance@test.invalid"),
            EmailHash = encryptionService.ComputeHmac("noinstance@test.invalid"),
            PasswordHash = "hashed",
            IsAdmin = false,
            IsDisabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = DateTimeOffset.UtcNow
        };
        dbContext.HubUsers.Add(user);
        await dbContext.SaveChangesAsync();

        var handler = new ChangePlanHandler(
            dbContext,
            StubUser(user.Id),
            NoStripeOptions(),
            new NoOpStripeService(),
            new AesEncryptionService(TestEncryptionKey),
            BuildConfiguration());

        var command = new ChangePlanCommand(999_000_000_000L, InstanceTier.Basic, MediaEnabled: true);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INSTANCE_NOT_FOUND");
    }
}
