using FluentAssertions;
using Microsoft.Extensions.Options;
using XcordHub.Entities;
using XcordHub.Features.Provisioning;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Infrastructure.Fixtures;
using Xunit;

namespace XcordHub.Tests.Infrastructure.Billing;

/// <summary>
/// Proves admin provisioning fails fast on paid tiers with no payment method:
/// without this, a paid instance is created with no Stripe subscription and
/// nothing ever charges for it (the register path already validated this; the
/// admin path did not).
/// </summary>
[Collection("SharedPostgres")]
[Trait("Category", "Integration")]
public sealed class ProvisionFailFastTests : BillingTestsBase
{
    public ProvisionFailFastTests(SharedPostgresFixture fixture)
        : base(fixture, "xcordhub_provisionfailfast_test")
    {
    }

    private ProvisionInstanceHandler BuildHandler(HubDbContext db, long userId, bool stripeConfigured)
        => new(
            db,
            new NoOpProvisioningQueue(),
            new SnowflakeIdGenerator(311),
            StubUser(userId),
            Options.Create(new AuthOptions { BcryptWorkFactor = 4 }),
            stripeConfigured ? FakeStripeOptions() : NoStripeOptions());

    [Fact]
    public async Task Provision_PaidTierWithoutPaymentMethod_IsRejected()
    {
        await using var db = CreateDbContext();
        var (user, _) = await SeedInstanceAsync(db, UserIdBase + 9001, "ff_nopm");
        var handler = BuildHandler(db, user.Id, stripeConfigured: true);

        var result = await handler.Handle(new ProvisionInstanceCommand(
            user.Id, "paid-nopm.example.com", "Paid", "password123!", InstanceTier.Pro), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("PAYMENT_METHOD_REQUIRED");
    }

    [Fact]
    public async Task Provision_PaidTierWithBillingExempt_SucceedsWithActiveBilling()
    {
        await using var db = CreateDbContext();
        var (user, _) = await SeedInstanceAsync(db, UserIdBase + 9002, "ff_exempt");
        var handler = BuildHandler(db, user.Id, stripeConfigured: true);

        var result = await handler.Handle(new ProvisionInstanceCommand(
            user.Id, "paid-exempt.example.com", "Exempt", "password123!", InstanceTier.Pro,
            BillingExempt: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var billing = db.InstanceBillings.Single(b => b.ManagedInstanceId == long.Parse(result.Value!.InstanceId));
        billing.BillingExempt.Should().BeTrue();
        billing.BillingStatus.Should().Be(BillingStatus.Active,
            "exempt instances are deliberately not billed, so they are never AwaitingPayment");
    }

    [Fact]
    public async Task Provision_PaidTierWithPaymentMethod_StartsAwaitingPayment()
    {
        await using var db = CreateDbContext();
        var (user, _) = await SeedInstanceAsync(db, UserIdBase + 9003, "ff_pm");
        var handler = BuildHandler(db, user.Id, stripeConfigured: true);

        var result = await handler.Handle(new ProvisionInstanceCommand(
            user.Id, "paid-pm.example.com", "Paid", "password123!", InstanceTier.Pro,
            PaymentMethodId: "pm_test_123"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var billing = db.InstanceBillings.Single(b => b.ManagedInstanceId == long.Parse(result.Value!.InstanceId));
        billing.BillingStatus.Should().Be(BillingStatus.AwaitingPayment,
            "billing only becomes Active once the Stripe subscription actually exists");
    }

    [Fact]
    public async Task Provision_PaidTierWithStripeUnconfigured_SucceedsActive()
    {
        await using var db = CreateDbContext();
        var (user, _) = await SeedInstanceAsync(db, UserIdBase + 9004, "ff_nostripe");
        var handler = BuildHandler(db, user.Id, stripeConfigured: false);

        var result = await handler.Handle(new ProvisionInstanceCommand(
            user.Id, "paid-nostripe.example.com", "Paid", "password123!", InstanceTier.Pro), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(
            "self-hosted hubs without Stripe must still be able to provision any tier");
    }
}
