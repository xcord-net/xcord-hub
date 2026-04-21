namespace XcordHub.Entities;

public sealed record ResourceLimits
{
    public int MaxUsers { get; init; }
    public int MaxServers { get; init; }
    public int MaxStorageMb { get; init; }
    public int MaxCpuPercent { get; init; }
    public int MaxMemoryMb { get; init; }
    public int MaxRateLimit { get; init; }
    public int MaxVoiceConcurrency { get; init; }
    public int MaxVideoConcurrency { get; init; }
    public int MaxConcurrentBroadcasts { get; init; }
    public int MaxStageSize { get; init; } = 8;
    public int MaxStreambotsPerChannel { get; init; } = 5;
    public int BroadcastMaxBitrateKbps { get; init; } = 4000;
    public int BroadcastMaxResolutionWidth { get; init; } = 1280;
    public int BroadcastMaxResolutionHeight { get; init; } = 720;
}
