using FluentAssertions;
using XcordHub.Entities;
using XcordHub.Features.Instances;

namespace XcordHub.Tests.Unit;

public sealed class TierDefaultsTests
{
    // ---------------------------------------------------------------------------
    // GetMaxUsers
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(InstanceTier.Free, 10)]
    [InlineData(InstanceTier.Basic, 50)]
    [InlineData(InstanceTier.Pro, 200)]
    [InlineData(InstanceTier.Enterprise, 500)]
    public void GetMaxUsers_ReturnsExpectedCount(InstanceTier tier, int expected)
    {
        TierDefaults.GetMaxUsers(tier).Should().Be(expected);
    }

    [Fact]
    public void GetMaxUsers_UnknownTier_ThrowsArgumentOutOfRange()
    {
        var act = () => TierDefaults.GetMaxUsers((InstanceTier)99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------------------------------------------------------------------------
    // GetResourceLimits - verify each tier returns sensible limits and
    // that limits increase monotonically across tiers
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(InstanceTier.Free)]
    [InlineData(InstanceTier.Basic)]
    [InlineData(InstanceTier.Pro)]
    [InlineData(InstanceTier.Enterprise)]
    public void GetResourceLimits_AllTiers_ReturnsNonNullWithPositiveValues(InstanceTier tier)
    {
        var limits = TierDefaults.GetResourceLimits(tier);

        limits.Should().NotBeNull();
        limits.MaxUsers.Should().BeGreaterThan(0);
        limits.MaxServers.Should().BeGreaterThan(0);
        limits.MaxStorageMb.Should().BeGreaterThan(0);
        limits.MaxCpuPercent.Should().BeGreaterThan(0);
        limits.MaxMemoryMb.Should().BeGreaterThan(0);
        limits.MaxRateLimit.Should().BeGreaterThan(0);
        limits.MaxVoiceConcurrency.Should().BeGreaterThan(0);
        limits.MaxVideoConcurrency.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetResourceLimits_MonotonicIncrease_AcrossAllTiers()
    {
        var free = TierDefaults.GetResourceLimits(InstanceTier.Free);
        var basic = TierDefaults.GetResourceLimits(InstanceTier.Basic);
        var pro = TierDefaults.GetResourceLimits(InstanceTier.Pro);
        var enterprise = TierDefaults.GetResourceLimits(InstanceTier.Enterprise);

        basic.MaxUsers.Should().BeGreaterThan(free.MaxUsers);
        pro.MaxUsers.Should().BeGreaterThan(basic.MaxUsers);
        enterprise.MaxUsers.Should().BeGreaterThan(pro.MaxUsers);

        basic.MaxServers.Should().BeGreaterThan(free.MaxServers);
        pro.MaxServers.Should().BeGreaterThan(basic.MaxServers);
        enterprise.MaxServers.Should().BeGreaterThan(pro.MaxServers);

        basic.MaxStorageMb.Should().BeGreaterThan(free.MaxStorageMb);
        pro.MaxStorageMb.Should().BeGreaterThan(basic.MaxStorageMb);
        enterprise.MaxStorageMb.Should().BeGreaterThan(pro.MaxStorageMb);

        basic.MaxCpuPercent.Should().BeGreaterThan(free.MaxCpuPercent);
        pro.MaxCpuPercent.Should().BeGreaterThan(basic.MaxCpuPercent);
        enterprise.MaxCpuPercent.Should().BeGreaterThan(pro.MaxCpuPercent);

        basic.MaxMemoryMb.Should().BeGreaterThan(free.MaxMemoryMb);
        pro.MaxMemoryMb.Should().BeGreaterThan(basic.MaxMemoryMb);
        enterprise.MaxMemoryMb.Should().BeGreaterThan(pro.MaxMemoryMb);

        basic.MaxRateLimit.Should().BeGreaterThan(free.MaxRateLimit);
        pro.MaxRateLimit.Should().BeGreaterThan(basic.MaxRateLimit);
        enterprise.MaxRateLimit.Should().BeGreaterThan(pro.MaxRateLimit);

        basic.MaxVoiceConcurrency.Should().BeGreaterThan(free.MaxVoiceConcurrency);
        pro.MaxVoiceConcurrency.Should().BeGreaterThan(basic.MaxVoiceConcurrency);
        enterprise.MaxVoiceConcurrency.Should().BeGreaterThan(pro.MaxVoiceConcurrency);

        basic.MaxVideoConcurrency.Should().BeGreaterThan(free.MaxVideoConcurrency);
        pro.MaxVideoConcurrency.Should().BeGreaterThan(basic.MaxVideoConcurrency);
        enterprise.MaxVideoConcurrency.Should().BeGreaterThan(pro.MaxVideoConcurrency);
    }

    [Fact]
    public void GetResourceLimits_UnknownTier_ThrowsArgumentOutOfRange()
    {
        var act = () => TierDefaults.GetResourceLimits((InstanceTier)99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------------------------------------------------------------------------
    // GetFeatureFlags - media disabled vs enabled
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(InstanceTier.Free)]
    [InlineData(InstanceTier.Basic)]
    [InlineData(InstanceTier.Pro)]
    [InlineData(InstanceTier.Enterprise)]
    public void GetFeatureFlags_WithoutMedia_DisablesVoiceAndVideo(InstanceTier tier)
    {
        var flags = TierDefaults.GetFeatureFlags(tier, mediaEnabled: false);

        flags.Should().NotBeNull();
        flags.CanCreateBots.Should().BeTrue();
        flags.CanUseWebhooks.Should().BeTrue();
        flags.CanUseCustomEmoji.Should().BeTrue();
        flags.CanUseThreads.Should().BeTrue();
        flags.CanUseForumChannels.Should().BeTrue();
        flags.CanUseScheduledEvents.Should().BeTrue();
        flags.CanUseVoiceChannels.Should().BeFalse();
        flags.CanUseVideoChannels.Should().BeFalse();
        flags.CanUseHdVideo.Should().BeFalse();
        flags.CanUseSimulcast.Should().BeFalse();
        flags.CanUseRecording.Should().BeFalse();
        flags.CanUseMemberTiers.Should().Be(tier >= InstanceTier.Pro);
    }

    [Theory]
    [InlineData(InstanceTier.Free)]
    [InlineData(InstanceTier.Basic)]
    [InlineData(InstanceTier.Pro)]
    [InlineData(InstanceTier.Enterprise)]
    public void GetFeatureFlags_WithMedia_EnablesVoiceAndVideo(InstanceTier tier)
    {
        var flags = TierDefaults.GetFeatureFlags(tier, mediaEnabled: true);

        flags.Should().NotBeNull();
        flags.CanCreateBots.Should().BeTrue();
        flags.CanUseWebhooks.Should().BeTrue();
        flags.CanUseCustomEmoji.Should().BeTrue();
        flags.CanUseThreads.Should().BeTrue();
        flags.CanUseForumChannels.Should().BeTrue();
        flags.CanUseScheduledEvents.Should().BeTrue();
        flags.CanUseVoiceChannels.Should().BeTrue();
        flags.CanUseVideoChannels.Should().BeTrue();
        flags.CanUseHdVideo.Should().BeTrue();
        flags.CanUseSimulcast.Should().BeTrue();
        flags.CanUseMemberTiers.Should().Be(tier >= InstanceTier.Pro);
    }

    [Theory]
    [InlineData(InstanceTier.Free, false)]
    [InlineData(InstanceTier.Basic, false)]
    [InlineData(InstanceTier.Pro, true)]
    [InlineData(InstanceTier.Enterprise, true)]
    public void GetFeatureFlags_Recording_OnlyProAndEnterprise(InstanceTier tier, bool expectedRecording)
    {
        var flags = TierDefaults.GetFeatureFlags(tier, mediaEnabled: true);
        flags.CanUseRecording.Should().Be(expectedRecording,
            $"{tier} with media should {(expectedRecording ? "" : "not ")}allow recording");
    }

    [Theory]
    [InlineData(InstanceTier.Free, false)]
    [InlineData(InstanceTier.Basic, false)]
    [InlineData(InstanceTier.Pro, true)]
    [InlineData(InstanceTier.Enterprise, true)]
    public void GetFeatureFlags_CanUseMemberTiers_OnlyProAndEnterprise(InstanceTier tier, bool expectedMemberTiers)
    {
        var flags = TierDefaults.GetFeatureFlags(tier, mediaEnabled: true);
        flags.CanUseMemberTiers.Should().Be(expectedMemberTiers,
            $"{tier} should {(expectedMemberTiers ? "" : "not ")}allow member tiers");
    }

    [Fact]
    public void GetFeatureFlags_UnknownTier_ThrowsArgumentOutOfRange()
    {
        var act = () => TierDefaults.GetFeatureFlags((InstanceTier)99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------------------------------------------------------------------------
    // GetBasePriceCents
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(InstanceTier.Free, 0)]
    [InlineData(InstanceTier.Basic, 6000)]
    [InlineData(InstanceTier.Pro, 15000)]
    [InlineData(InstanceTier.Enterprise, 30000)]
    public void GetBasePriceCents_ReturnsExpectedPrice(InstanceTier tier, int expectedCents)
    {
        TierDefaults.GetBasePriceCents(tier).Should().Be(expectedCents);
    }

    // ---------------------------------------------------------------------------
    // GetMediaPerUserCents
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(InstanceTier.Free, 400)]
    [InlineData(InstanceTier.Basic, 300)]
    [InlineData(InstanceTier.Pro, 200)]
    [InlineData(InstanceTier.Enterprise, 100)]
    public void GetMediaPerUserCents_ReturnsExpectedPrice(InstanceTier tier, int expectedCents)
    {
        TierDefaults.GetMediaPerUserCents(tier).Should().Be(expectedCents);
    }

    // ---------------------------------------------------------------------------
    // GetMediaPriceCents - perUser * maxUsers
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(InstanceTier.Free, 4000)]       // 400 * 10
    [InlineData(InstanceTier.Basic, 15000)]      // 300 * 50
    [InlineData(InstanceTier.Pro, 40000)]        // 200 * 200
    [InlineData(InstanceTier.Enterprise, 50000)] // 100 * 500
    public void GetMediaPriceCents_ReturnsExpectedPrice(InstanceTier tier, int expectedCents)
    {
        TierDefaults.GetMediaPriceCents(tier).Should().Be(expectedCents);
    }

    // ---------------------------------------------------------------------------
    // GetTotalPriceCents - base + optional media
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(InstanceTier.Free, false, 0)]
    [InlineData(InstanceTier.Free, true, 4000)]
    [InlineData(InstanceTier.Basic, false, 6000)]
    [InlineData(InstanceTier.Basic, true, 21000)]
    [InlineData(InstanceTier.Pro, false, 15000)]
    [InlineData(InstanceTier.Pro, true, 55000)]
    [InlineData(InstanceTier.Enterprise, false, 30000)]
    [InlineData(InstanceTier.Enterprise, true, 80000)]
    public void GetTotalPriceCents_ReturnsExpectedPrice(InstanceTier tier, bool mediaEnabled, int expectedCents)
    {
        TierDefaults.GetTotalPriceCents(tier, mediaEnabled).Should().Be(expectedCents,
            $"{tier} (media={mediaEnabled}) should cost {expectedCents} cents");
    }

    // ---------------------------------------------------------------------------
    // Cross-method consistency: all defined enum values work without throwing
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(InstanceTier.Free)]
    [InlineData(InstanceTier.Basic)]
    [InlineData(InstanceTier.Pro)]
    [InlineData(InstanceTier.Enterprise)]
    public void AllMethods_NeverThrow_ForAllDefinedTiers(InstanceTier tier)
    {
        var act1 = () => TierDefaults.GetResourceLimits(tier);
        var act2 = () => TierDefaults.GetFeatureFlags(tier);
        var act3 = () => TierDefaults.GetBasePriceCents(tier);
        var act4 = () => TierDefaults.GetMediaPerUserCents(tier);
        var act5 = () => TierDefaults.GetMediaPriceCents(tier);
        var act6 = () => TierDefaults.GetTotalPriceCents(tier, false);
        var act7 = () => TierDefaults.GetTotalPriceCents(tier, true);
        var act8 = () => TierDefaults.GetMaxUsers(tier);

        act1.Should().NotThrow();
        act2.Should().NotThrow();
        act3.Should().NotThrow();
        act4.Should().NotThrow();
        act5.Should().NotThrow();
        act6.Should().NotThrow();
        act7.Should().NotThrow();
        act8.Should().NotThrow();
    }
}
