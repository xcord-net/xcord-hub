using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XcordHub.Entities;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Provisioning;

/// <summary>
/// Creates a Stripe subscription for paid tier instances after provisioning succeeds.
/// This is the last step in the pipeline - the instance is running, so we charge.
/// If Stripe is not configured or the tier is free (without media), this step is a no-op.
/// </summary>
public sealed class CreateSubscriptionStep : IProvisioningStep
{
    private readonly HubDbContext _dbContext;
    private readonly IStripeService _stripeService;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<CreateSubscriptionStep> _logger;
    private readonly bool _stripeConfigured;

    public string StepName => "CreateSubscription";

    public CreateSubscriptionStep(
        HubDbContext dbContext,
        IStripeService stripeService,
        IEncryptionService encryptionService,
        IOptions<StripeOptions> stripeOptions,
        ILogger<CreateSubscriptionStep> logger)
    {
        _dbContext = dbContext;
        _stripeService = stripeService;
        _encryptionService = encryptionService;
        _logger = logger;
        _stripeConfigured = stripeOptions.Value.IsConfigured;
    }

    public async Task<Result<bool>> ExecuteAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.ManagedInstances
            .Include(i => i.Billing)
            .Include(i => i.Owner)
            .Include(i => i.Config)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance?.Billing == null || instance.Owner == null)
            return true; // No billing record or owner, skip

        var tier = instance.Billing.Tier;
        var mediaEnabled = instance.Billing.MediaEnabled;
        var priceCents = TierDefaults.GetTotalPriceCents(tier, mediaEnabled);

        // Skip if free without media, or Stripe not configured
        if (priceCents == 0 || !_stripeConfigured)
        {
            _logger.LogInformation("Skipping subscription creation for instance {InstanceId} (free tier or Stripe not configured)", instanceId);
            return true;
        }

        // Read payment method ID from config JSON (set during instance creation)
        string? paymentMethodId = null;
        if (instance.Config?.ConfigJson != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(instance.Config.ConfigJson);
                if (doc.RootElement.TryGetProperty("PaymentMethodId", out var pmElem))
                    paymentMethodId = pmElem.GetString();
            }
            catch { /* config JSON parse failure - skip */ }
        }
        if (string.IsNullOrWhiteSpace(paymentMethodId))
        {
            _logger.LogWarning("Instance {InstanceId} is a paid tier but no payment method was provided", instanceId);
            return true; // Don't fail provisioning - subscription can be created later via billing page
        }

        try
        {
            // Ensure Stripe customer exists
            var ownerEmail = _encryptionService.Decrypt(instance.Owner.Email);
            if (string.IsNullOrWhiteSpace(instance.Owner.StripeCustomerId))
            {
                instance.Owner.StripeCustomerId = await _stripeService.EnsureCustomerAsync(
                    instance.OwnerId, ownerEmail, instance.Owner.DisplayName, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            // Resolve the Stripe price ID from our lookup key convention
            var lookupKey = BuildStripePriceId(tier, mediaEnabled);
            var priceId = await _stripeService.ResolvePriceIdByLookupKeyAsync(lookupKey, cancellationToken);
            if (priceId == null)
            {
                _logger.LogError("Stripe price not found for lookup key {LookupKey}", lookupKey);
                return true; // Don't fail provisioning
            }

            // Create the subscription - charges the first invoice automatically
            var result = await _stripeService.CreateSubscriptionAsync(
                instance.Owner.StripeCustomerId,
                priceId,
                paymentMethodId,
                new Dictionary<string, string>
                {
                    ["instance_id"] = instanceId.ToString(),
                    ["domain"] = instance.Domain
                },
                cancellationToken);

            // Update billing record with subscription info
            instance.Billing.StripeSubscriptionId = result.SubscriptionId;
            instance.Billing.StripePriceId = priceId;
            instance.Billing.BillingStatus = BillingStatus.Active;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Created Stripe subscription {SubscriptionId} for instance {InstanceId} ({Domain})",
                result.SubscriptionId, instanceId, instance.Domain);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Stripe subscription for instance {InstanceId}", instanceId);
            // Don't fail provisioning - the instance is running. Subscription can be retried.
            return true;
        }
    }

    public Task<Result<bool>> VerifyAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        // No verification needed - if the subscription creation failed, the instance still runs
        return Task.FromResult<Result<bool>>(true);
    }

    private static string BuildStripePriceId(InstanceTier tier, bool mediaEnabled)
    {
        var suffix = mediaEnabled ? "_media" : "";
        return $"price_xcord_{tier.ToString().ToLowerInvariant()}{suffix}";
    }
}
