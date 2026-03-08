namespace XcordHub.Entities;

public sealed class UpgradeRollout
{
    public long Id { get; set; }
    public string? FromImage { get; set; }
    public string ToImage { get; set; } = string.Empty;
    public RolloutStatus Status { get; set; }
    public int TotalInstances { get; set; }
    public int CompletedInstances { get; set; }
    // Not a FK — the instance ID is preserved for diagnostics even if the instance is later destroyed.
    public long? FailedInstanceId { get; set; }
    public string? ErrorMessage { get; set; }
    public string? TargetPool { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long InitiatedBy { get; set; }

    // Navigation properties
    public HubUser Initiator { get; set; } = null!;
    public ICollection<UpgradeEvent> UpgradeEvents { get; set; } = new List<UpgradeEvent>();
}
