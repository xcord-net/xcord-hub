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
                MaxRateLimit = 30,
                MaxVoiceConcurrency = 5,
                MaxVideoConcurrency = 3
            },
            UserCountTier.Tier50 => new ResourceLimits
            {
                MaxUsers = 50,
                MaxServers = 10,
                MaxStorageMb = 2048,
                MaxCpuPercent = 50,
                MaxMemoryMb = 512,
                MaxRateLimit = 60,
                MaxVoiceConcurrency = 15,
                MaxVideoConcurrency = 10
            },
            UserCountTier.Tier100 => new ResourceLimits
            {
                MaxUsers = 100,
                MaxServers = 25,
                MaxStorageMb = 5120,
                MaxCpuPercent = 100,
                MaxMemoryMb = 1024,
                MaxRateLimit = 120,
                MaxVoiceConcurrency = 30,
                MaxVideoConcurrency = 20
            },
            UserCountTier.Tier500 => new ResourceLimits
            {
                MaxUsers = 500,
                MaxServers = 100,
                MaxStorageMb = 25600,
                MaxCpuPercent = 200,
                MaxMemoryMb = 2048,
                MaxRateLimit = 500,
                MaxVoiceConcurrency = 100,
                MaxVideoConcurrency = 50
            },
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown user count tier")
        };
    }

    public static FeatureFlags GetFeatureFlags(FeatureTier tier, bool hdUpgrade = false)
    {
        if (!Enum.IsDefined(tier))
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown feature tier");

        var isHd = hdUpgrade && tier == FeatureTier.Video;

        return new FeatureFlags
        {
            CanCreateBots = true,
            CanUseWebhooks = true,
            CanUseCustomEmoji = true,
            CanUseThreads = true,
            CanUseVoiceChannels = tier >= FeatureTier.Audio,
            CanUseVideoChannels = tier >= FeatureTier.Video,
            CanUseForumChannels = true,
            CanUseScheduledEvents = true,
            CanUseHdVideo = isHd,
            CanUseSimulcast = isHd,
            CanUseRecording = isHd
        };
    }

    public static int GetPriceCents(FeatureTier feature, UserCountTier users, bool hdUpgrade = false)
    {
        var baseCents = (feature, users) switch
        {
            (FeatureTier.Chat, UserCountTier.Tier10) => 0,
            (FeatureTier.Chat, UserCountTier.Tier50) => 20_00,
            (FeatureTier.Chat, UserCountTier.Tier100) => 60_00,
            (FeatureTier.Chat, UserCountTier.Tier500) => 200_00,
            (FeatureTier.Audio, UserCountTier.Tier10) => 20_00,
            (FeatureTier.Audio, UserCountTier.Tier50) => 45_00,
            (FeatureTier.Audio, UserCountTier.Tier100) => 110_00,
            (FeatureTier.Audio, UserCountTier.Tier500) => 400_00,
            (FeatureTier.Video, UserCountTier.Tier10) => 40_00,
            (FeatureTier.Video, UserCountTier.Tier50) => 70_00,
            (FeatureTier.Video, UserCountTier.Tier100) => 160_00,
            (FeatureTier.Video, UserCountTier.Tier500) => 550_00,
            _ => throw new ArgumentOutOfRangeException()
        };

        if (hdUpgrade && feature == FeatureTier.Video)
        {
            baseCents += GetHdUpgradePriceCents(users);
        }

        return baseCents;
    }

    public static int GetHdUpgradePriceCents(UserCountTier users)
    {
        return users switch
        {
            UserCountTier.Tier10 => 25_00,
            UserCountTier.Tier50 => 50_00,
            UserCountTier.Tier100 => 75_00,
            UserCountTier.Tier500 => 150_00,
            _ => throw new ArgumentOutOfRangeException(nameof(users), users, "Unknown user count tier")
        };
    }
}
