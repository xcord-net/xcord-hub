namespace XcordHub.Entities;

public sealed class InstanceRevenueConfig
{
    public long Id { get; set; }
    public long ManagedInstanceId { get; set; }
    public int DefaultRevenueSharePercent { get; set; } = 70;
    public int MinPlatformCutPercent { get; set; } = 30;
    public string? StripeConnectedAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navigation
    public ManagedInstance ManagedInstance { get; set; } = null!;
}
