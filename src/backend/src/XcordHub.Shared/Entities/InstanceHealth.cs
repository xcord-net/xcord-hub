namespace XcordHub.Entities;

public sealed class InstanceHealth
{
    public long Id { get; set; }
    public long ManagedInstanceId { get; set; }
    public bool IsHealthy { get; set; }
    public DateTimeOffset LastCheckAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int? ResponseTimeMs { get; set; }
    public string? ErrorMessage { get; set; }

    // Navigation properties
    public ManagedInstance ManagedInstance { get; set; } = null!;
}
