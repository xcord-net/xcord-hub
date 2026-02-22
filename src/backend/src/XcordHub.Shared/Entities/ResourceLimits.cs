namespace XcordHub.Entities;

public sealed record ResourceLimits
{
    public int MaxUsers { get; init; }
    public int MaxServers { get; init; }
    public int MaxStorageMb { get; init; }
    public int MaxCpuPercent { get; init; }
    public int MaxMemoryMb { get; init; }
    public int MaxRateLimit { get; init; }
}
