namespace XcordHub.Entities;

public sealed class UpgradeEvent
{
    public long Id { get; set; }
    public long? UpgradeRolloutId { get; set; }
    public long ManagedInstanceId { get; set; }
    public UpgradeEventStatus Status { get; set; }
    public string? PreviousImage { get; set; }
    public string TargetImage { get; set; } = string.Empty;
    public string? PreviousVersion { get; set; }
    public string? NewVersion { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    // Navigation properties
    public UpgradeRollout? Rollout { get; set; }
    public ManagedInstance ManagedInstance { get; set; } = null!;
}
