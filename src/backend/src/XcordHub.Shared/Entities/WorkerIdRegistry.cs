namespace XcordHub.Entities;

public sealed class WorkerIdRegistry
{
    public int WorkerId { get; set; }
    public long? ManagedInstanceId { get; set; }
    public bool IsTombstoned { get; set; }
    public DateTimeOffset AllocatedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }

    // Navigation properties
    public ManagedInstance? ManagedInstance { get; set; }
}
