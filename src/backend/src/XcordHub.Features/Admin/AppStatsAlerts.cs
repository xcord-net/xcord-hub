using XcordHub.Entities;

namespace XcordHub.Features.Admin;

/// <summary>
/// One managed instance, flattened to the fields the stats endpoint reasons
/// about. Projected in the handler's query so the alert rules below stay pure and
/// testable without a database.
/// </summary>
public sealed record InstanceStatsRow
{
    public required long Id { get; init; }

    /// <summary>The instance domain. An opaque identifier, and the one an operator acts on.</summary>
    public required string Domain { get; init; }

    public required InstanceStatus Status { get; init; }

    /// <summary>
    /// When provisioning was last enqueued, falling back to CreatedAt. Stuck
    /// detection measures from here and not from CreatedAt, which never resets
    /// across the reconciler's retries.
    /// </summary>
    public required DateTimeOffset ProvisioningSince { get; init; }

    public required int ProvisioningAttempts { get; init; }

    /// <summary>null when no health check has ever run for this instance.</summary>
    public bool? IsHealthy { get; init; }

    public int? ConsecutiveFailures { get; init; }

    public string? HealthError { get; init; }

    /// <summary>
    /// ErrorMessage from the most recent failed provisioning step, if any. A
    /// Failed instance usually has no health record at all - it never got far
    /// enough to be checked - so this is where its reason lives.
    /// </summary>
    public string? ProvisioningError { get; init; }

    public InstanceTier? Tier { get; init; }

    public bool MediaEnabled { get; init; }

    public BillingStatus? BillingStatus { get; init; }

    public bool BillingExempt { get; init; }
}

/// <summary>
/// The instance-fleet alert rules, as pure functions over
/// <see cref="InstanceStatsRow"/>.
///
/// Separated from the handler because these are the rules worth testing and the
/// handler is mostly EF queries: "stuck in Provisioning for eleven minutes" needs
/// to be a unit test, not a container.
/// </summary>
public static class AppStatsAlerts
{
    /// <summary>
    /// How long an instance may sit in Provisioning before it counts as stuck.
    /// The pipeline's own steps are seconds to a couple of minutes; ten minutes
    /// means a step is hung rather than slow.
    /// </summary>
    public static readonly TimeSpan ProvisioningStuckAfter = TimeSpan.FromMinutes(10);

    /// <summary>Detail lists are capped by the schema at 20 entries.</summary>
    private const int MaxDetail = 20;

    public static List<StatsAlert> ForInstances(IReadOnlyList<InstanceStatsRow> rows, DateTimeOffset now)
    {
        var alerts = new List<StatsAlert>();

        var failed = rows.Where(r => r.Status == InstanceStatus.Failed).ToList();
        if (failed.Count > 0)
        {
            alerts.Add(new StatsAlert
            {
                Severity = StatsSeverity.Crit,
                Code = "instances_failed",
                Message = failed.Count == 1
                    ? "1 instance is in Failed - it is not serving and will not retry on its own"
                    : $"{failed.Count} instances are in Failed - they are not serving and will not retry on their own",
                Count = failed.Count,
                Detail = failed
                    .OrderBy(r => r.Domain, StringComparer.Ordinal)
                    .Take(MaxDetail)
                    .Select(r => Cap(
                        $"{r.Domain} - {r.ProvisioningAttempts} provisioning attempt(s); {Reason(r)}", 200))
                    .ToList(),
                Href = "https://xcord.net/admin/instances?status=Failed"
            });
        }

        var stuck = rows
            .Where(r => r.Status == InstanceStatus.Provisioning &&
                        now - r.ProvisioningSince > ProvisioningStuckAfter)
            .OrderBy(r => r.ProvisioningSince)
            .ToList();
        if (stuck.Count > 0)
        {
            alerts.Add(new StatsAlert
            {
                Severity = StatsSeverity.Crit,
                Code = "instances_provisioning_stuck",
                Message = stuck.Count == 1
                    ? "1 instance has been Provisioning for over 10 minutes - a pipeline step is hung"
                    : $"{stuck.Count} instances have been Provisioning for over 10 minutes - a pipeline step is hung",
                Count = stuck.Count,
                OldestAgeS = (long)(now - stuck[0].ProvisioningSince).TotalSeconds,
                Detail = stuck
                    .Take(MaxDetail)
                    .Select(r => Cap(
                        $"{r.Domain} - provisioning for {Minutes(now - r.ProvisioningSince)}, " +
                        $"{r.ProvisioningAttempts} attempt(s)", 200))
                    .ToList(),
                Href = "https://xcord.net/admin/instances?status=Provisioning"
            });
        }

        var unhealthy = rows
            .Where(r => r.Status == InstanceStatus.Running && r.IsHealthy == false)
            .OrderByDescending(r => r.ConsecutiveFailures ?? 0)
            .ToList();
        if (unhealthy.Count > 0)
        {
            var worst = unhealthy[0].ConsecutiveFailures ?? 0;
            alerts.Add(new StatsAlert
            {
                Severity = StatsSeverity.Crit,
                Code = "instances_running_unhealthy",
                Message = unhealthy.Count == 1
                    ? $"1 Running instance is failing its health check ({worst} consecutive failures)"
                    : $"{unhealthy.Count} Running instances are failing their health checks " +
                      $"(worst: {worst} consecutive failures)",
                Count = unhealthy.Count,
                Detail = unhealthy
                    .Take(MaxDetail)
                    .Select(r => Cap(
                        $"{r.Domain} - {r.ConsecutiveFailures ?? 0} consecutive failures; {Reason(r)}", 200))
                    .ToList(),
                Href = "https://xcord.net/admin/instances?status=Running"
            });
        }

        return alerts;
    }

    /// <summary>
    /// The failure text, trimmed. ErrorMessage is operator-facing text written by
    /// the health checker or the provisioning pipeline; it carries no user data,
    /// but it can be long enough to blow the schema's per-item length cap on its
    /// own.
    /// </summary>
    private static string Reason(InstanceStatsRow row)
    {
        var reason = row.HealthError ?? row.ProvisioningError;
        return string.IsNullOrWhiteSpace(reason) ? "no error recorded" : Cap(reason.Trim(), 120);
    }

    /// <summary>
    /// The schema caps detail entries at 200 characters and alert messages at
    /// 300. A payload that overruns them is refused whole, so every string that
    /// contains a database value is cut here rather than at the console.
    /// </summary>
    internal static string Cap(string value, int max) =>
        value.Length <= max ? value : value[..(max - 3)] + "...";

    private static string Minutes(TimeSpan age) => $"{(int)age.TotalMinutes}m";
}
