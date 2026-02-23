using XcordHub.Entities;

namespace XcordHub.Features.Instances;

public static class TierDefaults
{
    public static ResourceLimits GetResourceLimits(UserCountTier tier)
    {
        return tier switch
        {
            UserCountTier.Tier10 => new ResourceLimits
            {
                MaxUsers = 10,
                MaxServers = 3,
                MaxStorageMb = 512,
                MaxCpuPercent = 25,
                MaxMemoryMb = 256,
                MaxRateLimit = 30
            },
            UserCountTier.Tier50 => new ResourceLimits
            {
                MaxUsers = 50,
                MaxServers = 10,
                MaxStorageMb = 2048,
                MaxCpuPercent = 50,
                MaxMemoryMb = 512,
                MaxRateLimit = 60
            },
            UserCountTier.Tier100 => new ResourceLimits
            {
                MaxUsers = 100,
                MaxServers = 25,
                MaxStorageMb = 5120,
                MaxCpuPercent = 100,
                MaxMemoryMb = 1024,
                MaxRateLimit = 120
            },
            UserCountTier.Tier500 => new ResourceLimits
            {
                MaxUsers = 500,
                MaxServers = 100,
                MaxStorageMb = 25600,
                MaxCpuPercent = 200,
                MaxMemoryMb = 2048,
                MaxRateLimit = 500
            },
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown user count tier")
        };
    }

    public static FeatureFlags GetFeatureFlags(FeatureTier tier)
    {
        if (!Enum.IsDefined(tier))
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown feature tier");

        return new FeatureFlags
        {
            CanCreateBots = true,
            CanUseWebhooks = true,
            CanUseCustomEmoji = true,
            CanUseThreads = true,
            CanUseVoiceChannels = tier >= FeatureTier.Audio,
            CanUseVideoChannels = tier >= FeatureTier.Video,
            CanUseForumChannels = true,
            CanUseScheduledEvents = true
        };
    }

    public static int GetPriceCents(FeatureTier feature, UserCountTier users)
    {
        return (feature, users) switch
        {
            (FeatureTier.Chat, UserCountTier.Tier10) => 0,
            (FeatureTier.Chat, UserCountTier.Tier50) => 20_00,
            (FeatureTier.Chat, UserCountTier.Tier100) => 40_00,
            (FeatureTier.Chat, UserCountTier.Tier500) => 130_00,
            (FeatureTier.Audio, UserCountTier.Tier10) => 20_00,
            (FeatureTier.Audio, UserCountTier.Tier50) => 45_00,
            (FeatureTier.Audio, UserCountTier.Tier100) => 85_00,
            (FeatureTier.Audio, UserCountTier.Tier500) => 260_00,
            (FeatureTier.Video, UserCountTier.Tier10) => 40_00,
            (FeatureTier.Video, UserCountTier.Tier50) => 70_00,
            (FeatureTier.Video, UserCountTier.Tier100) => 125_00,
            (FeatureTier.Video, UserCountTier.Tier500) => 350_00,
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}
