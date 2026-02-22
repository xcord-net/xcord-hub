using FluentAssertions;
using XcordHub.Entities;
using XcordHub.Features.Instances;

namespace XcordHub.Tests.Unit;

/// <summary>
/// Unit tests for TierDefaults — the static class that maps BillingTier values to
/// resource limits, feature flags, and per-tier instance quotas.
///
/// These tests exercise the logic that CreateInstanceHandler relies on to enforce
/// billing tier assignment and quota checks (issue #240: billing tier was hardcoded
/// to Free; the handler now reads user.SubscriptionTier and passes it through).
/// </summary>
public sealed class TierDefaultsTests
{
    // ---------------------------------------------------------------------------
    // GetMaxInstancesForTier
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(BillingTier.Free, 1)]
    [InlineData(BillingTier.Pro, 10)]
    public void GetMaxInstancesForTier_BoundedTiers_ReturnExpectedLimit(BillingTier tier, int expectedMax)
    {
        var result = TierDefaults.GetMaxInstancesForTier(tier);
        result.Should().Be(expectedMax,
            $"{tier} tier should allow exactly {expectedMax} instance(s)");
    }

    [Fact]
    public void GetMaxInstancesForTier_Enterprise_ReturnsUnlimited()
    {
        var result = TierDefaults.GetMaxInstancesForTier(BillingTier.Enterprise);
        result.Should().Be(-1, "Enterprise tier should have no instance limit (-1 = unlimited)");
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
    public void GetResourceLimits_ProTier_ExceedsFreeOnAllBoundedLimits()
    {
        var freeLimits = TierDefaults.GetResourceLimits(BillingTier.Free);
        var proLimits = TierDefaults.GetResourceLimits(BillingTier.Pro);

        proLimits.MaxUsers.Should().BeGreaterThan(freeLimits.MaxUsers,
            "Pro tier should allow more users than Free");
        proLimits.MaxServers.Should().BeGreaterThan(freeLimits.MaxServers,
            "Pro tier should allow more servers than Free");
        proLimits.MaxStorageMb.Should().BeGreaterThan(freeLimits.MaxStorageMb,
            "Pro tier should have more storage than Free");
        proLimits.MaxCpuPercent.Should().BeGreaterThan(freeLimits.MaxCpuPercent,
            "Pro tier should have higher CPU limit than Free");
        proLimits.MaxMemoryMb.Should().BeGreaterThan(freeLimits.MaxMemoryMb,
            "Pro tier should have more memory than Free");
        proLimits.MaxRateLimit.Should().BeGreaterThan(freeLimits.MaxRateLimit,
            "Pro tier should have higher rate limit than Free");
    }

    [Fact]
    public void GetResourceLimits_EnterpriseTier_ReturnsUnlimitedLimits()
    {
        var limits = TierDefaults.GetResourceLimits(BillingTier.Enterprise);

        limits.MaxUsers.Should().Be(-1, "Enterprise tier should have unlimited users");
        limits.MaxServers.Should().Be(-1, "Enterprise tier should have unlimited servers");
        limits.MaxStorageMb.Should().Be(-1, "Enterprise tier should have unlimited storage");
        limits.MaxCpuPercent.Should().Be(-1, "Enterprise tier should have unlimited CPU");
        limits.MaxMemoryMb.Should().Be(-1, "Enterprise tier should have unlimited memory");
        limits.MaxRateLimit.Should().Be(-1, "Enterprise tier should have unlimited rate limit");
    }

    [Fact]
    public void GetResourceLimits_UnknownTier_ThrowsArgumentOutOfRange()
    {
        var act = () => TierDefaults.GetResourceLimits((BillingTier)99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------------------------------------------------------------------------
    // GetFeatureFlags — verify tier escalation unlocks features
    // ---------------------------------------------------------------------------

    [Fact]
    public void GetFeatureFlags_FreeTier_RestrictsAdvancedFeatures()
    {
        var flags = TierDefaults.GetFeatureFlags(BillingTier.Free);

        flags.Should().NotBeNull();
        // Advanced features not available on Free
        flags.CanCreateBots.Should().BeFalse("bots require a paid tier");
        flags.CanUseWebhooks.Should().BeFalse("webhooks require a paid tier");
        flags.CanUseCustomEmoji.Should().BeFalse("custom emoji require a paid tier");
        flags.CanUseVideoChannels.Should().BeFalse("video channels require a paid tier");
        flags.CanUseForumChannels.Should().BeFalse("forum channels require a paid tier");
        flags.CanUseScheduledEvents.Should().BeFalse("scheduled events require a paid tier");
    }

    [Fact]
    public void GetFeatureFlags_FreeTier_AllowsBasicFeatures()
    {
        var flags = TierDefaults.GetFeatureFlags(BillingTier.Free);

        // Basic features available even on Free
        flags.CanUseThreads.Should().BeTrue("threads should be available on Free tier");
        flags.CanUseVoiceChannels.Should().BeTrue("voice channels should be available on Free tier");
    }

    [Theory]
    [InlineData(BillingTier.Pro)]
    [InlineData(BillingTier.Enterprise)]
    public void GetFeatureFlags_PaidTiers_UnlockAllAdvancedFeatures(BillingTier tier)
    {
        var flags = TierDefaults.GetFeatureFlags(tier);

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
    [InlineData(BillingTier.Pro)]
    [InlineData(BillingTier.Enterprise)]
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
