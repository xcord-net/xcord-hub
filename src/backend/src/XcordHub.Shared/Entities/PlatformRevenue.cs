namespace XcordHub.Entities;

public sealed class PlatformRevenue
{
    public long Id { get; set; }
    public long ManagedInstanceId { get; set; }
    public int AmountCents { get; set; }
    public int PlatformFeeCents { get; set; }
    public int OwnerPayoutCents { get; set; }
    public string? StripeTransferId { get; set; }
    public string Currency { get; set; } = "usd";
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation
    public ManagedInstance ManagedInstance { get; set; } = null!;
}
