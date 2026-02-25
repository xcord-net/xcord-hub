namespace XcordHub.Entities;

public sealed record FeatureFlags
{
    public bool CanCreateBots { get; init; }
    public bool CanUseWebhooks { get; init; }
    public bool CanUseCustomEmoji { get; init; }
    public bool CanUseThreads { get; init; }
    public bool CanUseVoiceChannels { get; init; }
    public bool CanUseVideoChannels { get; init; }
    public bool CanUseForumChannels { get; init; }
    public bool CanUseScheduledEvents { get; init; }
    public bool CanUseHdVideo { get; init; }
    public bool CanUseSimulcast { get; init; }
    public bool CanUseRecording { get; init; }
}
