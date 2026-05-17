namespace XcordHub.Entities;

public sealed record FeatureFlags
{
    public bool CanUseVoiceChannels { get; init; }
    public bool CanUseVideoChannels { get; init; }
    public bool CanUseSimulcast { get; init; }
    public bool CanUseMemberTiers { get; init; }
    public bool CanBroadcast { get; init; }
}
