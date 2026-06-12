namespace XcordHub.Entities;

public sealed class InstanceBilling
{
    public long Id { get; set; }
    public long ManagedInstanceId { get; set; }
    public InstanceTier Tier { get; set; }
    public bool MediaEnabled { get; set; }
    public BillingStatus BillingStatus { get; set; }
    public bool BillingExempt { get; set; }
    public string? StripePriceId { get; set; }
    public string? StripeSubscriptionId { get; set; }

    /// <summary>
    /// For Enterprise metered billing: the Stripe subscription item ID used when reporting usage.
    /// Null for flat-rate subscriptions.
    /// </summary>
    public string? StripeSubscriptionItemId { get; set; }

    /// <summary>
    /// True when the Enterprise instance uses usage-based (metered) billing instead of flat rate.
    /// Only applicable when Tier == Enterprise.
    /// </summary>
    public bool IsMeteredBilling { get; set; }

    public DateTimeOffset? CurrentPeriodEnd { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }

    /// <summary>
    /// When <see cref="BillingStatus"/> last changed. The BillingEnforcer's
    /// grace period is measured from this timestamp.
    /// </summary>
    public DateTimeOffset BillingStatusChangedAt { get; set; }

    /// <summary>
    /// True when the BillingEnforcer suspended the instance for non-payment.
    /// Guards webhook-driven resume so a payment can never resume an instance
    /// that was suspended manually for other reasons.
    /// </summary>
    public bool BillingSuspended { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // Navigation properties
    public ManagedInstance ManagedInstance { get; set; } = null!;
}
