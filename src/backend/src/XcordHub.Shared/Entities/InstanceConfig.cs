namespace XcordHub.Entities;

public sealed class InstanceConfig
{
    public long Id { get; set; }
    public long ManagedInstanceId { get; set; }
    public string ConfigJson { get; set; } = string.Empty;
    public string ResourceLimitsJson { get; set; } = string.Empty;
    public string FeatureFlagsJson { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation properties
    public ManagedInstance ManagedInstance { get; set; } = null!;
}
