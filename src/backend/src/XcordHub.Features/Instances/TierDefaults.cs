using XcordHub.Entities;

namespace XcordHub.Features.Instances;

public static class TierDefaults
{
    public static ResourceLimits GetResourceLimits(BillingTier tier)
    {
        return tier switch
        {
            BillingTier.Free => new ResourceLimits
            {
                MaxUsers = 100,
                MaxServers = 5,
                MaxStorageMb = 1024, // 1 GB
                MaxCpuPercent = 50,
                MaxMemoryMb = 512,
                MaxRateLimit = 100
            },
            BillingTier.Pro => new ResourceLimits
            {
                MaxUsers = 10000,
                MaxServers = 50,
                MaxStorageMb = 10240, // 10 GB
                MaxCpuPercent = 200,
                MaxMemoryMb = 2048,
                MaxRateLimit = 1000
            },
            BillingTier.Enterprise => new ResourceLimits
            {
                MaxUsers = -1, // unlimited
                MaxServers = -1, // unlimited
                MaxStorageMb = -1, // unlimited
                MaxCpuPercent = -1, // unlimited
                MaxMemoryMb = -1, // unlimited
                MaxRateLimit = -1 // unlimited
            },
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown billing tier")
        };
    }

    public static FeatureFlags GetFeatureFlags(BillingTier tier)
    {
        return tier switch
        {
            BillingTier.Free => new FeatureFlags
            {
                CanCreateBots = false,
                CanUseWebhooks = false,
                CanUseCustomEmoji = false,
                CanUseThreads = true,
                CanUseVoiceChannels = true,
                CanUseVideoChannels = false,
                CanUseForumChannels = false,
                CanUseScheduledEvents = false
            },
            BillingTier.Pro => new FeatureFlags
            {
                CanCreateBots = true,
                CanUseWebhooks = true,
                CanUseCustomEmoji = true,
                CanUseThreads = true,
                CanUseVoiceChannels = true,
                CanUseVideoChannels = true,
                CanUseForumChannels = true,
                CanUseScheduledEvents = true
            },
            BillingTier.Enterprise => new FeatureFlags
            {
                CanCreateBots = true,
                CanUseWebhooks = true,
                CanUseCustomEmoji = true,
                CanUseThreads = true,
                CanUseVoiceChannels = true,
                CanUseVideoChannels = true,
                CanUseForumChannels = true,
                CanUseScheduledEvents = true
            },
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown billing tier")
        };
    }

    public static int GetMaxInstancesForTier(BillingTier tier)
    {
        return tier switch
        {
            BillingTier.Free => 1,
            BillingTier.Pro => 10,
            BillingTier.Enterprise => -1, // unlimited
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown billing tier")
        };
    }
}
