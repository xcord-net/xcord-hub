namespace XcordHub.Entities;

public sealed class UpgradeRollout
{
    public long Id { get; set; }
    public string? FromImage { get; set; }
    public string ToImage { get; set; } = string.Empty;
    public RolloutStatus Status { get; set; }
    public int TotalInstances { get; set; }
    public int CompletedInstances { get; set; }
    public int BatchSize { get; set; } = 5;
    public int MaxFailures { get; set; } = 1;
    public DateTimeOffset? ScheduledAt { get; set; }
    public int FailedInstances { get; set; }
    public string? TargetPool { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long InitiatedBy { get; set; }

    // Navigation properties
    public HubUser Initiator { get; set; } = null!;
    public ICollection<UpgradeEvent> UpgradeEvents { get; set; } = new List<UpgradeEvent>();
}
