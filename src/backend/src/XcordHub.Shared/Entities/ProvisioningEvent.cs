using XcordHub.Entities;

namespace XcordHub.Entities;

public sealed class ProvisioningEvent
{
    public long Id { get; set; }
    public long ManagedInstanceId { get; set; }
    public ProvisioningPhase Phase { get; set; }
    public string StepName { get; set; } = string.Empty;
    public ProvisioningStepStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    // Navigation properties
    public ManagedInstance ManagedInstance { get; set; } = null!;
}
