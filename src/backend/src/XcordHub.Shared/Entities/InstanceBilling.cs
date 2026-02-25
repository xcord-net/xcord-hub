namespace XcordHub.Entities;

public sealed class InstanceBilling
{
    public long Id { get; set; }
    public long ManagedInstanceId { get; set; }
    public FeatureTier FeatureTier { get; set; }
    public UserCountTier UserCountTier { get; set; }
    public bool HdUpgrade { get; set; }
    public BillingStatus BillingStatus { get; set; }
    public bool BillingExempt { get; set; }
    public string? StripePriceId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public DateTimeOffset? CurrentPeriodEnd { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation properties
    public ManagedInstance ManagedInstance { get; set; } = null!;
}
