using FluentAssertions;
using XcordHub.Entities;
using XcordHub.Features.Admin;
using Xunit;

namespace XcordHub.Tests.Unit;

/// <summary>
/// The instance-fleet alert rules behind /api/v1/admin/stats.
///
/// These are the payload's most important field: they are what the console
/// renders above everything else and counts on the tab's own label, so a rule
/// that quietly stops firing is a problem nobody is told about.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AppStatsAlertsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    private static InstanceStatsRow Instance(
        InstanceStatus status,
        string domain = "alpha.xcord.net",
        DateTimeOffset? provisioningSince = null,
        bool? isHealthy = true,
        int? consecutiveFailures = 0,
        string? healthError = null,
        string? provisioningError = null,
        int attempts = 1) => new()
        {
            Id = 1,
            Domain = domain,
            Status = status,
            ProvisioningSince = provisioningSince ?? Now,
            ProvisioningAttempts = attempts,
            IsHealthy = isHealthy,
            ConsecutiveFailures = consecutiveFailures,
            HealthError = healthError,
            ProvisioningError = provisioningError
        };

    // --- stuck in Provisioning ---------------------------------------------

    [Fact]
    public void ProvisioningLongerThanTenMinutes_RaisesACriticalAlert()
    {
        var rows = new[]
        {
            Instance(InstanceStatus.Provisioning,
                domain: "hung.xcord.net",
                provisioningSince: Now.AddMinutes(-11),
                isHealthy: null,
                consecutiveFailures: null,
                attempts: 2)
        };

        var alerts = AppStatsAlerts.ForInstances(rows, Now);

        var stuck = alerts.Single(a => a.Code == "instances_provisioning_stuck");
        stuck.Severity.Should().Be("crit");
        stuck.Count.Should().Be(1);
        stuck.OldestAgeS.Should().Be(660, "the age lets an operator tell 11 minutes from 3 days");
        stuck.Detail.Should().ContainSingle()
            .Which.Should().Contain("hung.xcord.net", "an alert you cannot trace to a record cannot be acted on")
            .And.Contain("2 attempt(s)");
    }

    [Fact]
    public void ProvisioningInsideTenMinutes_IsNotAnAlert()
    {
        // The pipeline's own steps take seconds to a couple of minutes. Alerting
        // on a healthy provision is how an operator learns to ignore the band.
        var rows = new[]
        {
            Instance(InstanceStatus.Provisioning, provisioningSince: Now.AddMinutes(-9), isHealthy: null)
        };

        AppStatsAlerts.ForInstances(rows, Now)
            .Should().NotContain(a => a.Code == "instances_provisioning_stuck");
    }

    [Fact]
    public void StuckDetectionMeasuresFromTheLastAttempt_NotFromCreation()
    {
        // ProvisioningSince is LastProvisioningAttemptAt, which the reconciler
        // resets on every retry. Measuring from CreatedAt would report a
        // just-retried instance as having been stuck for days.
        var rows = new[]
        {
            Instance(InstanceStatus.Provisioning, provisioningSince: Now.AddMinutes(-2), isHealthy: null, attempts: 3)
        };

        AppStatsAlerts.ForInstances(rows, Now).Should().BeEmpty();
    }

    [Fact]
    public void TheOldestStuckInstanceSetsTheAge()
    {
        var rows = new[]
        {
            Instance(InstanceStatus.Provisioning, "recent.xcord.net", Now.AddMinutes(-15), isHealthy: null),
            Instance(InstanceStatus.Provisioning, "ancient.xcord.net", Now.AddHours(-30), isHealthy: null)
        };

        var stuck = AppStatsAlerts.ForInstances(rows, Now).Single(a => a.Code == "instances_provisioning_stuck");

        stuck.Count.Should().Be(2);
        stuck.OldestAgeS.Should().Be((long)TimeSpan.FromHours(30).TotalSeconds);
        stuck.Detail![0].Should().Contain("ancient.xcord.net");
    }

    // --- Failed ------------------------------------------------------------

    [Fact]
    public void FailedInstances_RaiseACriticalAlertNamingTheProvisioningError()
    {
        var rows = new[]
        {
            Instance(InstanceStatus.Failed,
                domain: "broken.xcord.net",
                isHealthy: null,
                consecutiveFailures: null,
                provisioningError: "network xcord-net-broken already exists",
                attempts: 3)
        };

        var failed = AppStatsAlerts.ForInstances(rows, Now).Single(a => a.Code == "instances_failed");

        failed.Severity.Should().Be("crit");
        failed.Detail.Should().ContainSingle()
            .Which.Should().Contain("broken.xcord.net").And.Contain("already exists");
    }

    // --- Running but unhealthy ----------------------------------------------

    [Fact]
    public void RunningButUnhealthy_RaisesACriticalAlertWithTheFailureCount()
    {
        var rows = new[]
        {
            Instance(InstanceStatus.Running,
                domain: "sick.xcord.net",
                isHealthy: false,
                consecutiveFailures: 7,
                healthError: "502 from https://sick.xcord.net/health")
        };

        var unhealthy = AppStatsAlerts.ForInstances(rows, Now).Single(a => a.Code == "instances_running_unhealthy");

        unhealthy.Severity.Should().Be("crit");
        unhealthy.Message.Should().Contain("7");
        unhealthy.Detail.Should().ContainSingle()
            .Which.Should().Contain("sick.xcord.net").And.Contain("502");
    }

    [Fact]
    public void AnInstanceThatHasNeverBeenCheckedIsNotReportedAsUnhealthy()
    {
        // IsHealthy is null when no health check has run. Treating "unknown" as
        // "unhealthy" would raise a crit for every instance in its first minute.
        var rows = new[] { Instance(InstanceStatus.Running, isHealthy: null, consecutiveFailures: null) };

        AppStatsAlerts.ForInstances(rows, Now).Should().BeEmpty();
    }

    [Fact]
    public void AHealthyFleetProducesNoAlerts()
    {
        var rows = new[]
        {
            Instance(InstanceStatus.Running, "one.xcord.net"),
            Instance(InstanceStatus.Running, "two.xcord.net"),
            Instance(InstanceStatus.Suspended, "three.xcord.net", isHealthy: null)
        };

        AppStatsAlerts.ForInstances(rows, Now).Should().BeEmpty();
    }

    [Fact]
    public void DetailStaysInsideTheSchemasLengthCap()
    {
        // The schema caps a detail entry at 200 characters and refuses the whole
        // payload if one overruns, so a long ErrorMessage is cut here.
        var rows = new[]
        {
            Instance(InstanceStatus.Running,
                domain: "verbose.xcord.net",
                isHealthy: false,
                consecutiveFailures: 5,
                healthError: new string('x', 4000))
        };

        var alert = AppStatsAlerts.ForInstances(rows, Now).Single();

        alert.Message.Length.Should().BeLessThanOrEqualTo(300);
        alert.Detail!.Should().OnlyContain(d => d.Length <= 200);
    }
}
