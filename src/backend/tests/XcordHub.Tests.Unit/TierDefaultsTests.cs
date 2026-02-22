using FluentAssertions;
using XcordHub.Entities;
using XcordHub.Features.Instances;

namespace XcordHub.Tests.Unit;

/// <summary>
/// Unit tests for TierDefaults — the static class that maps BillingTier values to
/// resource limits, feature flags, and per-tier instance quotas.
/// </summary>
public sealed class TierDefaultsTests
{
    // ---------------------------------------------------------------------------
    // GetMaxInstancesForTier
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(BillingTier.Free, 1)]
    [InlineData(BillingTier.Basic, 1)]
    [InlineData(BillingTier.Pro, 1)]
    public void GetMaxInstancesForTier_ReturnsExpectedLimit(BillingTier tier, int expectedMax)
    {
        var result = TierDefaults.GetMaxInstancesForTier(tier);
        result.Should().Be(expectedMax,
            $"{tier} tier should allow exactly {expectedMax} instance(s)");
    }

    [Fact]
    public void GetMaxInstancesForTier_UnknownTier_ThrowsArgumentOutOfRange()
    {
        var act = () => TierDefaults.GetMaxInstancesForTier((BillingTier)99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------------------------------------------------------------------------
    // GetResourceLimits — verify each tier returns non-null limits with sensible ordering
    // ---------------------------------------------------------------------------

    [Fact]
    public void GetResourceLimits_FreeTier_ReturnsRestrictiveLimits()
    {
        var limits = TierDefaults.GetResourceLimits(BillingTier.Free);

        limits.Should().NotBeNull();
        limits.MaxUsers.Should().BeGreaterThan(0, "Free tier must allow at least some users");
        limits.MaxServers.Should().BeGreaterThan(0, "Free tier must allow at least some servers");
        limits.MaxStorageMb.Should().BeGreaterThan(0, "Free tier must allow some storage");
        limits.MaxCpuPercent.Should().BeGreaterThan(0);
        limits.MaxMemoryMb.Should().BeGreaterThan(0);
        limits.MaxRateLimit.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetResourceLimits_BasicTier_ExceedsFreeOnAllLimits()
    {
        var freeLimits = TierDefaults.GetResourceLimits(BillingTier.Free);
        var basicLimits = TierDefaults.GetResourceLimits(BillingTier.Basic);

        basicLimits.MaxUsers.Should().BeGreaterThan(freeLimits.MaxUsers,
            "Basic tier should allow more users than Free");
        basicLimits.MaxServers.Should().BeGreaterThan(freeLimits.MaxServers,
            "Basic tier should allow more servers than Free");
        basicLimits.MaxStorageMb.Should().BeGreaterThan(freeLimits.MaxStorageMb,
            "Basic tier should have more storage than Free");
        basicLimits.MaxCpuPercent.Should().BeGreaterThan(freeLimits.MaxCpuPercent,
            "Basic tier should have higher CPU limit than Free");
        basicLimits.MaxMemoryMb.Should().BeGreaterThan(freeLimits.MaxMemoryMb,
            "Basic tier should have more memory than Free");
        basicLimits.MaxRateLimit.Should().BeGreaterThan(freeLimits.MaxRateLimit,
            "Basic tier should have higher rate limit than Free");
    }

    [Fact]
    public void GetResourceLimits_ProTier_HasHighestBoundedLimits()
    {
        var basicLimits = TierDefaults.GetResourceLimits(BillingTier.Basic);
        var proLimits = TierDefaults.GetResourceLimits(BillingTier.Pro);

        proLimits.MaxUsers.Should().BeGreaterThan(basicLimits.MaxUsers,
            "Pro tier should allow more users than Basic");
        proLimits.MaxServers.Should().BeGreaterThan(basicLimits.MaxServers,
            "Pro tier should allow more servers than Basic");
        proLimits.MaxStorageMb.Should().BeGreaterThan(basicLimits.MaxStorageMb,
            "Pro tier should have more storage than Basic");
        proLimits.MaxCpuPercent.Should().BeGreaterThan(basicLimits.MaxCpuPercent,
            "Pro tier should have higher CPU limit than Basic");
        proLimits.MaxMemoryMb.Should().BeGreaterThan(basicLimits.MaxMemoryMb,
            "Pro tier should have more memory than Basic");
        proLimits.MaxRateLimit.Should().BeGreaterThan(basicLimits.MaxRateLimit,
            "Pro tier should have higher rate limit than Basic");

        // Pro is bounded — no unlimited (-1) values
        proLimits.MaxUsers.Should().BeGreaterThan(0, "Pro tier should have bounded (not unlimited) users");
        proLimits.MaxServers.Should().BeGreaterThan(0, "Pro tier should have bounded (not unlimited) servers");
        proLimits.MaxStorageMb.Should().BeGreaterThan(0, "Pro tier should have bounded (not unlimited) storage");
    }

    [Fact]
    public void GetResourceLimits_UnknownTier_ThrowsArgumentOutOfRange()
    {
        var act = () => TierDefaults.GetResourceLimits((BillingTier)99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------------------------------------------------------------------------
    // GetFeatureFlags — Free tier has no video; Basic/Pro have all features
    // ---------------------------------------------------------------------------

    [Fact]
    public void GetFeatureFlags_FreeTier_DisablesVideo()
    {
        var flags = TierDefaults.GetFeatureFlags(BillingTier.Free);

        flags.Should().NotBeNull();
        flags.CanCreateBots.Should().BeTrue("Free should allow bots");
        flags.CanUseWebhooks.Should().BeTrue("Free should allow webhooks");
        flags.CanUseCustomEmoji.Should().BeTrue("Free should allow custom emoji");
        flags.CanUseThreads.Should().BeTrue("Free should allow threads");
        flags.CanUseVoiceChannels.Should().BeTrue("Free should allow voice channels");
        flags.CanUseVideoChannels.Should().BeFalse("Free should not allow video channels");
        flags.CanUseForumChannels.Should().BeTrue("Free should allow forum channels");
        flags.CanUseScheduledEvents.Should().BeTrue("Free should allow scheduled events");
    }

    [Theory]
    [InlineData(BillingTier.Basic)]
    [InlineData(BillingTier.Pro)]
    public void GetFeatureFlags_PaidTiers_EnableAllFeatures(BillingTier tier)
    {
        var flags = TierDefaults.GetFeatureFlags(tier);

        flags.Should().NotBeNull();
        flags.CanCreateBots.Should().BeTrue($"{tier} should allow bots");
        flags.CanUseWebhooks.Should().BeTrue($"{tier} should allow webhooks");
        flags.CanUseCustomEmoji.Should().BeTrue($"{tier} should allow custom emoji");
        flags.CanUseThreads.Should().BeTrue($"{tier} should allow threads");
        flags.CanUseVoiceChannels.Should().BeTrue($"{tier} should allow voice channels");
        flags.CanUseVideoChannels.Should().BeTrue($"{tier} should allow video channels");
        flags.CanUseForumChannels.Should().BeTrue($"{tier} should allow forum channels");
        flags.CanUseScheduledEvents.Should().BeTrue($"{tier} should allow scheduled events");
    }

    [Fact]
    public void GetFeatureFlags_UnknownTier_ThrowsArgumentOutOfRange()
    {
        var act = () => TierDefaults.GetFeatureFlags((BillingTier)99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------------------------------------------------------------------------
    // Cross-method consistency: verify all three methods agree on the tier list
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(BillingTier.Free)]
    [InlineData(BillingTier.Basic)]
    [InlineData(BillingTier.Pro)]
    public void AllThreeMethods_ReturnsValueForEveryDefinedTier(BillingTier tier)
    {
        // Each method should return without throwing for all defined tiers.
        var act1 = () => TierDefaults.GetMaxInstancesForTier(tier);
        var act2 = () => TierDefaults.GetResourceLimits(tier);
        var act3 = () => TierDefaults.GetFeatureFlags(tier);

        act1.Should().NotThrow($"GetMaxInstancesForTier must handle {tier}");
        act2.Should().NotThrow($"GetResourceLimits must handle {tier}");
        act3.Should().NotThrow($"GetFeatureFlags must handle {tier}");
    }
}
