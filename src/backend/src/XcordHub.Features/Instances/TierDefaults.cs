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
                MaxUsers = 50,
                MaxServers = 5,
                MaxStorageMb = 1024, // 1 GB
                MaxCpuPercent = 50,
                MaxMemoryMb = 512,
                MaxRateLimit = 60
            },
            BillingTier.Basic => new ResourceLimits
            {
                MaxUsers = 250,
                MaxServers = 25,
                MaxStorageMb = 10240, // 10 GB
                MaxCpuPercent = 100,
                MaxMemoryMb = 1024,
                MaxRateLimit = 150
            },
            BillingTier.Pro => new ResourceLimits
            {
                MaxUsers = 1000,
                MaxServers = 100,
                MaxStorageMb = 51200, // 50 GB
                MaxCpuPercent = 200,
                MaxMemoryMb = 2048,
                MaxRateLimit = 500
            },
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown billing tier")
        };
    }

    public static FeatureFlags GetFeatureFlags(BillingTier tier)
    {
        if (!Enum.IsDefined(tier))
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown billing tier");

        return new FeatureFlags
        {
            CanCreateBots = true,
            CanUseWebhooks = true,
            CanUseCustomEmoji = true,
            CanUseThreads = true,
            CanUseVoiceChannels = true,
            CanUseVideoChannels = tier != BillingTier.Free,
            CanUseForumChannels = true,
            CanUseScheduledEvents = true
        };
    }

    public static int GetMaxInstancesForTier(BillingTier tier)
    {
        return tier switch
        {
            BillingTier.Free => 1,
            BillingTier.Basic => 1,
            BillingTier.Pro => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown billing tier")
        };
    }
}
