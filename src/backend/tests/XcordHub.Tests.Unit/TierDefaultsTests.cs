using FluentAssertions;
using XcordHub.Entities;
using XcordHub.Features.Instances;

namespace XcordHub.Tests.Unit;

/// <summary>
/// Unit tests for TierDefaults — the static class that maps FeatureTier and UserCountTier
/// values to resource limits, feature flags, and prices.
/// </summary>
public sealed class TierDefaultsTests
{
    // ---------------------------------------------------------------------------
    // GetResourceLimits — verify each UserCountTier returns sensible limits and
    // that limits increase monotonically across tiers
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(UserCountTier.Tier10)]
    [InlineData(UserCountTier.Tier50)]
    [InlineData(UserCountTier.Tier100)]
    [InlineData(UserCountTier.Tier500)]
    public void GetResourceLimits_AllTiers_ReturnsNonNullWithPositiveValues(UserCountTier tier)
    {
        var limits = TierDefaults.GetResourceLimits(tier);

        limits.Should().NotBeNull();
        limits.MaxUsers.Should().BeGreaterThan(0, $"{tier} must allow at least some users");
        limits.MaxServers.Should().BeGreaterThan(0, $"{tier} must allow at least some servers");
        limits.MaxStorageMb.Should().BeGreaterThan(0, $"{tier} must allow some storage");
        limits.MaxCpuPercent.Should().BeGreaterThan(0, $"{tier} must have a CPU limit");
        limits.MaxMemoryMb.Should().BeGreaterThan(0, $"{tier} must have a memory limit");
        limits.MaxRateLimit.Should().BeGreaterThan(0, $"{tier} must have a rate limit");
    }

    [Fact]
    public void GetResourceLimits_MonotonicIncrease_AcrossAllTiers()
    {
        var t10 = TierDefaults.GetResourceLimits(UserCountTier.Tier10);
        var t50 = TierDefaults.GetResourceLimits(UserCountTier.Tier50);
        var t100 = TierDefaults.GetResourceLimits(UserCountTier.Tier100);
        var t500 = TierDefaults.GetResourceLimits(UserCountTier.Tier500);

        t50.MaxUsers.Should().BeGreaterThan(t10.MaxUsers, "Tier50 should allow more users than Tier10");
        t100.MaxUsers.Should().BeGreaterThan(t50.MaxUsers, "Tier100 should allow more users than Tier50");
        t500.MaxUsers.Should().BeGreaterThan(t100.MaxUsers, "Tier500 should allow more users than Tier100");

        t50.MaxServers.Should().BeGreaterThan(t10.MaxServers, "Tier50 should allow more servers than Tier10");
        t100.MaxServers.Should().BeGreaterThan(t50.MaxServers, "Tier100 should allow more servers than Tier50");
        t500.MaxServers.Should().BeGreaterThan(t100.MaxServers, "Tier500 should allow more servers than Tier100");

        t50.MaxStorageMb.Should().BeGreaterThan(t10.MaxStorageMb, "Tier50 should have more storage than Tier10");
        t100.MaxStorageMb.Should().BeGreaterThan(t50.MaxStorageMb, "Tier100 should have more storage than Tier50");
        t500.MaxStorageMb.Should().BeGreaterThan(t100.MaxStorageMb, "Tier500 should have more storage than Tier100");

        t50.MaxCpuPercent.Should().BeGreaterThan(t10.MaxCpuPercent, "Tier50 should have higher CPU than Tier10");
        t100.MaxCpuPercent.Should().BeGreaterThan(t50.MaxCpuPercent, "Tier100 should have higher CPU than Tier50");
        t500.MaxCpuPercent.Should().BeGreaterThan(t100.MaxCpuPercent, "Tier500 should have higher CPU than Tier100");

        t50.MaxMemoryMb.Should().BeGreaterThan(t10.MaxMemoryMb, "Tier50 should have more memory than Tier10");
        t100.MaxMemoryMb.Should().BeGreaterThan(t50.MaxMemoryMb, "Tier100 should have more memory than Tier50");
        t500.MaxMemoryMb.Should().BeGreaterThan(t100.MaxMemoryMb, "Tier500 should have more memory than Tier100");

        t50.MaxRateLimit.Should().BeGreaterThan(t10.MaxRateLimit, "Tier50 should have higher rate limit than Tier10");
        t100.MaxRateLimit.Should().BeGreaterThan(t50.MaxRateLimit, "Tier100 should have higher rate limit than Tier50");
        t500.MaxRateLimit.Should().BeGreaterThan(t100.MaxRateLimit, "Tier500 should have higher rate limit than Tier100");
    }

    [Fact]
    public void GetResourceLimits_UnknownTier_ThrowsArgumentOutOfRange()
    {
        var act = () => TierDefaults.GetResourceLimits((UserCountTier)99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------------------------------------------------------------------------
    // GetFeatureFlags — Chat has no voice/video; Audio has voice but no video;
    // Video has all features enabled
    // ---------------------------------------------------------------------------

    [Fact]
    public void GetFeatureFlags_ChatTier_DisablesVoiceAndVideo()
    {
        var flags = TierDefaults.GetFeatureFlags(FeatureTier.Chat);

        flags.Should().NotBeNull();
        flags.CanCreateBots.Should().BeTrue("Chat tier should allow bots");
        flags.CanUseWebhooks.Should().BeTrue("Chat tier should allow webhooks");
        flags.CanUseCustomEmoji.Should().BeTrue("Chat tier should allow custom emoji");
        flags.CanUseThreads.Should().BeTrue("Chat tier should allow threads");
        flags.CanUseForumChannels.Should().BeTrue("Chat tier should allow forum channels");
        flags.CanUseScheduledEvents.Should().BeTrue("Chat tier should allow scheduled events");
        flags.CanUseVoiceChannels.Should().BeFalse("Chat tier should not allow voice channels");
        flags.CanUseVideoChannels.Should().BeFalse("Chat tier should not allow video channels");
    }

    [Fact]
    public void GetFeatureFlags_AudioTier_EnablesVoiceButNotVideo()
    {
        var flags = TierDefaults.GetFeatureFlags(FeatureTier.Audio);

        flags.Should().NotBeNull();
        flags.CanCreateBots.Should().BeTrue("Audio tier should allow bots");
        flags.CanUseWebhooks.Should().BeTrue("Audio tier should allow webhooks");
        flags.CanUseCustomEmoji.Should().BeTrue("Audio tier should allow custom emoji");
        flags.CanUseThreads.Should().BeTrue("Audio tier should allow threads");
        flags.CanUseForumChannels.Should().BeTrue("Audio tier should allow forum channels");
        flags.CanUseScheduledEvents.Should().BeTrue("Audio tier should allow scheduled events");
        flags.CanUseVoiceChannels.Should().BeTrue("Audio tier should allow voice channels");
        flags.CanUseVideoChannels.Should().BeFalse("Audio tier should not allow video channels");
    }

    [Fact]
    public void GetFeatureFlags_VideoTier_EnablesAllFeatures()
    {
        var flags = TierDefaults.GetFeatureFlags(FeatureTier.Video);

        flags.Should().NotBeNull();
        flags.CanCreateBots.Should().BeTrue("Video tier should allow bots");
        flags.CanUseWebhooks.Should().BeTrue("Video tier should allow webhooks");
        flags.CanUseCustomEmoji.Should().BeTrue("Video tier should allow custom emoji");
        flags.CanUseThreads.Should().BeTrue("Video tier should allow threads");
        flags.CanUseForumChannels.Should().BeTrue("Video tier should allow forum channels");
        flags.CanUseScheduledEvents.Should().BeTrue("Video tier should allow scheduled events");
        flags.CanUseVoiceChannels.Should().BeTrue("Video tier should allow voice channels");
        flags.CanUseVideoChannels.Should().BeTrue("Video tier should allow video channels");
    }

    [Fact]
    public void GetFeatureFlags_UnknownTier_ThrowsArgumentOutOfRange()
    {
        var act = () => TierDefaults.GetFeatureFlags((FeatureTier)99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------------------------------------------------------------------------
    // GetPriceCents — all 12 combinations of FeatureTier x UserCountTier
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(FeatureTier.Chat,  UserCountTier.Tier10,  0)]
    [InlineData(FeatureTier.Chat,  UserCountTier.Tier50,  2000)]
    [InlineData(FeatureTier.Chat,  UserCountTier.Tier100, 4000)]
    [InlineData(FeatureTier.Chat,  UserCountTier.Tier500, 13000)]
    [InlineData(FeatureTier.Audio, UserCountTier.Tier10,  2000)]
    [InlineData(FeatureTier.Audio, UserCountTier.Tier50,  4500)]
    [InlineData(FeatureTier.Audio, UserCountTier.Tier100, 8500)]
    [InlineData(FeatureTier.Audio, UserCountTier.Tier500, 26000)]
    [InlineData(FeatureTier.Video, UserCountTier.Tier10,  4000)]
    [InlineData(FeatureTier.Video, UserCountTier.Tier50,  7000)]
    [InlineData(FeatureTier.Video, UserCountTier.Tier100, 12500)]
    [InlineData(FeatureTier.Video, UserCountTier.Tier500, 35000)]
    public void GetPriceCents_ReturnsExpectedPrice(FeatureTier featureTier, UserCountTier userCountTier, int expectedCents)
    {
        var price = TierDefaults.GetPriceCents(featureTier, userCountTier);
        price.Should().Be(expectedCents,
            $"({featureTier}, {userCountTier}) should cost {expectedCents} cents");
    }

    [Fact]
    public void GetPriceCents_UnknownFeatureTier_ThrowsArgumentOutOfRange()
    {
        var act = () => TierDefaults.GetPriceCents((FeatureTier)99, UserCountTier.Tier10);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetPriceCents_UnknownUserCountTier_ThrowsArgumentOutOfRange()
    {
        var act = () => TierDefaults.GetPriceCents(FeatureTier.Chat, (UserCountTier)99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------------------------------------------------------------------------
    // Cross-method consistency: all defined enum values work without throwing
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(UserCountTier.Tier10)]
    [InlineData(UserCountTier.Tier50)]
    [InlineData(UserCountTier.Tier100)]
    [InlineData(UserCountTier.Tier500)]
    public void GetResourceLimits_NeverThrows_ForAllDefinedUserCountTiers(UserCountTier tier)
    {
        var act = () => TierDefaults.GetResourceLimits(tier);
        act.Should().NotThrow($"GetResourceLimits must handle {tier}");
    }

    [Theory]
    [InlineData(FeatureTier.Chat)]
    [InlineData(FeatureTier.Audio)]
    [InlineData(FeatureTier.Video)]
    public void GetFeatureFlags_NeverThrows_ForAllDefinedFeatureTiers(FeatureTier tier)
    {
        var act = () => TierDefaults.GetFeatureFlags(tier);
        act.Should().NotThrow($"GetFeatureFlags must handle {tier}");
    }

    [Theory]
    [InlineData(FeatureTier.Chat,  UserCountTier.Tier10)]
    [InlineData(FeatureTier.Chat,  UserCountTier.Tier50)]
    [InlineData(FeatureTier.Chat,  UserCountTier.Tier100)]
    [InlineData(FeatureTier.Chat,  UserCountTier.Tier500)]
    [InlineData(FeatureTier.Audio, UserCountTier.Tier10)]
    [InlineData(FeatureTier.Audio, UserCountTier.Tier50)]
    [InlineData(FeatureTier.Audio, UserCountTier.Tier100)]
    [InlineData(FeatureTier.Audio, UserCountTier.Tier500)]
    [InlineData(FeatureTier.Video, UserCountTier.Tier10)]
    [InlineData(FeatureTier.Video, UserCountTier.Tier50)]
    [InlineData(FeatureTier.Video, UserCountTier.Tier100)]
    [InlineData(FeatureTier.Video, UserCountTier.Tier500)]
    public void GetPriceCents_NeverThrows_ForAllDefinedCombinations(FeatureTier featureTier, UserCountTier userCountTier)
    {
        var act = () => TierDefaults.GetPriceCents(featureTier, userCountTier);
        act.Should().NotThrow($"GetPriceCents must handle ({featureTier}, {userCountTier})");
    }
}
