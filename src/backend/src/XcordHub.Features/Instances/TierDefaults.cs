using XcordHub.Entities;

namespace XcordHub.Features.Instances;

public static class TierDefaults
{
    public static int GetMaxUsers(InstanceTier tier)
    {
        return tier switch
        {
            InstanceTier.Free => 10,
            InstanceTier.Basic => 50,
            InstanceTier.Pro => 200,
            InstanceTier.Enterprise => 500,
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown instance tier")
        };
    }

    public static ResourceLimits GetResourceLimits(InstanceTier tier)
    {
        return tier switch
        {
            InstanceTier.Free => new ResourceLimits
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
            InstanceTier.Basic => new ResourceLimits
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
            InstanceTier.Pro => new ResourceLimits
            {
                MaxUsers = 200,
                MaxServers = 50,
                MaxStorageMb = 10240,
                MaxCpuPercent = 150,
                MaxMemoryMb = 1536,
                MaxRateLimit = 200,
                MaxVoiceConcurrency = 60,
                MaxVideoConcurrency = 40
            },
            InstanceTier.Enterprise => new ResourceLimits
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
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown instance tier")
        };
    }

    public static FeatureFlags GetFeatureFlags(InstanceTier tier, bool mediaEnabled = false)
    {
        if (!Enum.IsDefined(tier))
            throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown instance tier");

        return new FeatureFlags
        {
            CanCreateBots = true,
            CanUseWebhooks = true,
            CanUseCustomEmoji = true,
            CanUseThreads = true,
            CanUseForumChannels = true,
            CanUseScheduledEvents = true,
            CanUseVoiceChannels = mediaEnabled,
            CanUseVideoChannels = mediaEnabled,
            CanUseHdVideo = mediaEnabled,
            CanUseSimulcast = mediaEnabled,
            CanUseRecording = mediaEnabled && tier >= InstanceTier.Pro
        };
    }

    public static int GetBasePriceCents(InstanceTier tier)
    {
        return tier switch
        {
            InstanceTier.Free => 0,
            InstanceTier.Basic => 60_00,
            InstanceTier.Pro => 150_00,
            InstanceTier.Enterprise => 300_00,
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown instance tier")
        };
    }

    public static int GetMediaPerUserCents(InstanceTier tier)
    {
        return tier switch
        {
            InstanceTier.Free => 4_00,
            InstanceTier.Basic => 3_00,
            InstanceTier.Pro => 2_00,
            InstanceTier.Enterprise => 1_00,
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown instance tier")
        };
    }

    public static int GetMediaPriceCents(InstanceTier tier)
    {
        return GetMediaPerUserCents(tier) * GetMaxUsers(tier);
    }

    public static int GetTotalPriceCents(InstanceTier tier, bool mediaEnabled = false)
    {
        var total = GetBasePriceCents(tier);
        if (mediaEnabled)
            total += GetMediaPriceCents(tier);
        return total;
    }
}
