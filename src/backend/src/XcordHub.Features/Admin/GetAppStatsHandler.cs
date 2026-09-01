using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Admin;

public sealed record GetAppStatsQuery();

/// <summary>
/// GET /api/v1/admin/stats - what xcord tells the spark console.
///
/// Implements schema 2 of the app-stats contract. Three rules shape everything
/// below and are worth stating once:
///
///   * `null` means NOT TRACKED and is never 0. Zero failed upgrades and no
///     upgrade tracking look identical on a dashboard and mean opposite things.
///   * A section that cannot be computed carries `error` and no rows, rather
///     than being dropped - an omitted section renders like an empty one, so
///     "cannot see" would read as "nothing is wrong".
///   * No personal data. Instance domains, record ids and IP COUNTS are fine;
///     the submitter's name, their email and the text they wrote are not, and
///     the contact-submission queries below select ids and timestamps only.
///
/// Cost: the console polls every 30 seconds per open tab. Everything here is a
/// COUNT or a small bounded read against an indexed column, and the two reads
/// that are not - the unreported uptime intervals and the instance list - are
/// bounded by the size of the fleet.
/// </summary>
public sealed class GetAppStatsHandler(
    HubDbContext dbContext,
    IConfiguration configuration,
    ILogger<GetAppStatsHandler> logger)
    : IRequestHandler<GetAppStatsQuery, Result<AppStatsResponse>>
{
    /// <summary>
    /// Worker IDs 11-1023: 0-10 are reserved for infrastructure and the top of
    /// the range is the Snowflake layout's 10-bit limit. They are TOMBSTONED on
    /// destroy rather than recycled, so this pool only ever shrinks.
    /// </summary>
    private const int WorkerIdCapacity = 1013;

    /// <summary>
    /// The console polls every 30s; nothing here is cached, so anything older
    /// than a minute means the console is not reaching us rather than that our
    /// numbers are stale.
    /// </summary>
    private const int MaxAgeSeconds = 60;

    /// <summary>Row caps, so a growing fleet cannot grow the payload without bound.</summary>
    private const int MaxRows = 20;

    public async Task<Result<AppStatsResponse>> Handle(GetAppStatsQuery request, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var now = DateTimeOffset.UtcNow;

        var alerts = new List<StatsAlert>();
        var headline = new List<StatsHeadline>();
        var sections = new List<StatsSection>();

        // --- the fleet ------------------------------------------------------
        // Loaded once: the alerts, the headline figures and the Instances
        // section are three views of the same rows, and three round trips for
        // one answer is how a 30-second poll becomes expensive.
        IReadOnlyList<InstanceStatsRow> instances = [];
        string? instancesError = null;
        try
        {
            instances = await LoadInstancesAsync(ct).ConfigureAwait(false);
            alerts.AddRange(AppStatsAlerts.ForInstances(instances, now));
        }
        catch (Exception ex) when (NotCancellation(ex, ct))
        {
            instancesError = Describe(ex, "instances");
        }

        // --- leads ----------------------------------------------------------
        LeadStats? leads = null;
        string? leadsError = null;
        try
        {
            leads = await LoadLeadsAsync(now, ct).ConfigureAwait(false);
            if (leads.AgedCount > 0)
            {
                alerts.Add(new StatsAlert
                {
                    Severity = StatsSeverity.Warn,
                    Code = "contact_submissions_unanswered",
                    Message = $"{leads.AgedCount} enterprise enquir{(leads.AgedCount == 1 ? "y has" : "ies have")} " +
                              "been sitting for over 24h - contact submissions are emailed to sales@ and " +
                              "nothing marks one answered, so this is every one older than a day",
                    Count = leads.AgedCount,
                    OldestAgeS = leads.OldestAgeSeconds,
                    Detail = leads.AgedIds
                });
            }
        }
        catch (Exception ex) when (NotCancellation(ex, ct))
        {
            leadsError = Describe(ex, "leads");
        }

        // --- upgrades -------------------------------------------------------
        UpgradeStats? upgrades = null;
        string? upgradesError = null;
        try
        {
            upgrades = await LoadUpgradesAsync(now, ct).ConfigureAwait(false);
            if (upgrades.Failed.Count > 0)
            {
                alerts.Add(new StatsAlert
                {
                    Severity = StatsSeverity.Warn,
                    Code = "upgrades_failed_7d",
                    Message = $"{upgrades.Failed.Count} upgrade{(upgrades.Failed.Count == 1 ? "" : "s")} " +
                              "failed in the last 7 days - those instances are still on their previous image",
                    Count = upgrades.Failed.Count,
                    Detail = upgrades.Failed
                        .Take(MaxRows)
                        .Select(u => AppStatsAlerts.Cap(
                            $"{u.Domain} -> {u.TargetImage}: {u.ErrorMessage ?? "no error recorded"}", 200))
                        .ToList()
                });
            }
        }
        catch (Exception ex) when (NotCancellation(ex, ct))
        {
            upgradesError = Describe(ex, "upgrades");
        }

        // --- uptime earned and not billed ------------------------------------
        UptimeStats? uptime = null;
        string? uptimeError = null;
        try
        {
            uptime = await LoadUptimeAsync(ct).ConfigureAwait(false);
            if (uptime.UnreportedIntervals > 0)
            {
                alerts.Add(new StatsAlert
                {
                    Severity = StatsSeverity.Warn,
                    Code = "uptime_unreported_to_stripe",
                    Message = $"{uptime.UnreportedMinutes:N0} minutes of metered uptime across " +
                              $"{uptime.UnreportedIntervals} closed intervals have never been reported - " +
                              "revenue earned and not billed. Stripe is unconfigured, so the hourly " +
                              "reporter returns before it reads them and this only grows",
                    Count = uptime.UnreportedIntervals,
                    OldestAgeS = uptime.OldestAgeSeconds
                });
            }
        }
        catch (Exception ex) when (NotCancellation(ex, ct))
        {
            uptimeError = Describe(ex, "uptime");
        }

        // --- headline --------------------------------------------------------
        var running = instances.Count(i => i.Status == InstanceStatus.Running);
        var notRunning = instances.Count - running;

        headline.Add(new StatsHeadline
        {
            Label = "Instances running",
            Value = instancesError is null ? running : null,
            Unit = StatsUnit.Count,
            Window = "now"
        });
        headline.Add(new StatsHeadline
        {
            Label = "Instances not running",
            Value = instancesError is null ? notRunning : null,
            Unit = StatsUnit.Count,
            Window = "now",
            State = notRunning > 0 ? StatsSeverity.Warn : StatsSeverity.Ok
        });
        headline.Add(new StatsHeadline
        {
            // Point in time, NOT a 30d sum: MRR is what the fleet bills per month
            // as of right now, and displaying it against a trailing window would
            // make a tier change look like revenue that had already been earned.
            Label = "MRR",
            Value = instancesError is null ? MonthlyRecurringCents(instances) : null,
            Unit = StatsUnit.Cents,
            AsOf = Iso(now)
        });
        headline.Add(new StatsHeadline
        {
            // Denominated in minutes because that is the unit the Stripe meter
            // (xcord_instance_uptime_minutes) is denominated in, and this figure
            // exists to be reconciled against it.
            Label = "Unbilled uptime (min)",
            Value = uptime?.UnreportedMinutes,
            State = (uptime?.UnreportedMinutes ?? 0) > 0 ? StatsSeverity.Warn : StatsSeverity.Ok,
            Window = "all"
        });
        headline.Add(new StatsHeadline
        {
            Label = "Leads awaiting reply",
            Value = leads?.Total,
            Unit = StatsUnit.Count,
            Window = "all",
            State = (leads?.AgedCount ?? 0) > 0 ? StatsSeverity.Warn : StatsSeverity.Ok
        });

        // --- sections ---------------------------------------------------------
        // Every one of these is built inside its own guard. A section that throws
        // reports `error` and the rest of the payload still arrives: an operator
        // who loses the Capacity query should not also lose the alert band.
        sections.Add(SafeSection("Instances", () => InstancesSection(instances, instancesError)));
        sections.Add(await SafeSectionAsync("Provisioning · 7d",
            c => ProvisioningSectionAsync(now, c), ct).ConfigureAwait(false));
        sections.Add(SafeSection("Leads", () => LeadsSection(leads, leadsError)));
        sections.Add(await SafeSectionAsync("Auth · 24h",
            c => AuthSectionAsync(now, c), ct).ConfigureAwait(false));
        sections.Add(await SafeSectionAsync("Tenant backups · 7d",
            c => BackupsSectionAsync(now, c), ct).ConfigureAwait(false));
        sections.Add(SafeSection("Upgrades",
            () => UpgradesSection(upgrades, upgradesError, uptime, uptimeError)));
        sections.Add(await SafeSectionAsync("Capacity",
            c => CapacitySectionAsync(instances, c), ct).ConfigureAwait(false));

        return new AppStatsResponse
        {
            Version = DeployedVersion(),
            GeneratedAt = Iso(now),
            MaxAgeSeconds = MaxAgeSeconds,
            ComputedMs = (int)clock.ElapsedMilliseconds,
            Alerts = alerts,
            Posture = Posture(),
            Headline = headline,
            Sections = sections
        };
    }

    // ======================================================================
    // posture - the effective state, not the configured value
    // ======================================================================

    private List<StatsPosture> Posture()
    {
        var posture = new List<StatsPosture>();

        // Stripe. IsConfigured is SecretKey being non-empty, and it is the same
        // flag StripeWebhookHandler and ReportUsageToStripeService test, so this
        // reports what those two will actually do rather than what a config file
        // hints at.
        var stripeKey = configuration["Stripe:SecretKey"];
        var stripeWebhookSecret = configuration["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(stripeKey))
        {
            posture.Add(new StatsPosture
            {
                Label = "Stripe",
                State = StatsSeverity.Crit,
                Value = "unconfigured",
                Detail = "no secret key: the webhook answers 503 to every event, the hourly usage " +
                         "reporter returns before reading a single interval, and no subscription " +
                         "is ever created - hosting is provisioned and nothing is charged"
            });
        }
        else if (string.IsNullOrWhiteSpace(stripeWebhookSecret))
        {
            posture.Add(new StatsPosture
            {
                Label = "Stripe",
                State = StatsSeverity.Crit,
                Value = "key set, webhook secret unset",
                Detail = "signature verification cannot run, so the webhook answers 503: money is " +
                         "collected and no instance ever leaves AwaitingPayment"
            });
        }
        else
        {
            posture.Add(new StatsPosture { Label = "Stripe", State = StatsSeverity.Ok, Value = "configured" });
        }

        // The hub's own alarm path. WebhookAlertService is the only caller, it
        // fires at exactly 5 consecutive health-check failures, and with no URL
        // it logs "No webhook URL configured, skipping health alert" and drops
        // the alarm - the instance stays down and nobody is told.
        var alertUrl = configuration["Alerting:WebhookUrl"];
        posture.Add(string.IsNullOrWhiteSpace(alertUrl)
            ? new StatsPosture
            {
                Label = "Health alerting",
                State = StatsSeverity.Crit,
                Value = "unset",
                Detail = "Alerting:WebhookUrl is empty, so every 5-consecutive-failure alarm is logged " +
                         "and dropped. An instance can be down indefinitely with nothing raised"
            }
            : new StatsPosture
            {
                Label = "Health alerting",
                State = string.IsNullOrWhiteSpace(configuration["Alerting:WebhookToken"])
                    ? StatsSeverity.Warn
                    : StatsSeverity.Ok,
                Value = HostOf(alertUrl),
                Detail = string.IsNullOrWhiteSpace(configuration["Alerting:WebhookToken"])
                    ? "URL set but no bearer token: the spark's notify acceptor answers 401 and the " +
                      "alarm is logged as a failed POST"
                    : null
            });

        // Mail. DevMode is the switch that decides whether SmtpEmailService sends
        // or writes to the log, which is the difference between a password-reset
        // link arriving and vanishing.
        var devMode = configuration.GetValue("Email:DevMode", false);
        var smtpHost = configuration["Email:SmtpHost"];
        var fromAddress = configuration["Email:FromAddress"];
        if (devMode)
        {
            posture.Add(new StatsPosture
            {
                Label = "Mail",
                State = StatsSeverity.Crit,
                Value = "dev mode",
                Detail = "Email:DevMode is true: confirmation codes, 2FA codes and password-reset links " +
                         "are written to the log and never sent"
            });
        }
        else if (string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(fromAddress))
        {
            posture.Add(new StatsPosture
            {
                Label = "Mail",
                State = StatsSeverity.Crit,
                Value = "incomplete",
                Detail = "SMTP host or from-address is empty, so every queued email throws on send"
            });
        }
        else
        {
            posture.Add(new StatsPosture
            {
                Label = "Mail",
                State = StatsSeverity.Ok,
                Value = $"smtp {smtpHost}",
                Detail = $"sending as {fromAddress}"
            });
        }

        // Registry. The hub NAMES an image and the Docker daemon pulls it; there
        // is no registry-credential setting anywhere in the hub, so a private
        // registry can only work if the daemon is already logged in - something
        // this process cannot see, let alone fix.
        var instanceImage = configuration["Docker:InstanceImage"];
        if (string.IsNullOrWhiteSpace(instanceImage))
            instanceImage = "xcord-fed:latest";
        var firstSegment = instanceImage.Split('/')[0];
        var namesARegistry = instanceImage.Contains('/') &&
                             (firstSegment.Contains('.') || firstSegment.Contains(':'));
        posture.Add(new StatsPosture
        {
            Label = "Instance registry",
            State = namesARegistry ? StatsSeverity.Warn : StatsSeverity.Info,
            Value = instanceImage,
            Detail = namesARegistry
                ? $"the instance image is pulled from {firstSegment}, and the hub holds no registry " +
                  "credentials - authentication is the daemon's, and a pull failure surfaces only as a " +
                  "failed StartApiContainer step"
                : "a bare local tag: provisioning can only use an image the Docker daemon already " +
                  "holds, and no registry - therefore no registry auth - is configured on the hub"
        });

        return posture;
    }

    // ======================================================================
    // queries
    // ======================================================================

    private async Task<IReadOnlyList<InstanceStatsRow>> LoadInstancesAsync(CancellationToken ct)
    {
        return await dbContext.ManagedInstances
            .AsNoTracking()
            .Where(i => i.DeletedAt == null)
            .Select(i => new InstanceStatsRow
            {
                Id = i.Id,
                Domain = i.Domain,
                Status = i.Status,
                ProvisioningSince = i.LastProvisioningAttemptAt ?? i.CreatedAt,
                ProvisioningAttempts = i.ProvisioningAttempts,
                IsHealthy = i.Health != null ? i.Health.IsHealthy : (bool?)null,
                ConsecutiveFailures = i.Health != null ? i.Health.ConsecutiveFailures : (int?)null,
                HealthError = i.Health != null ? i.Health.ErrorMessage : null,
                ProvisioningError = i.ProvisioningEvents
                    .Where(e => e.Status == ProvisioningStepStatus.Failed)
                    .OrderByDescending(e => e.Id)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault(),
                Tier = i.Billing != null ? i.Billing.Tier : (InstanceTier?)null,
                MediaEnabled = i.Billing != null && i.Billing.MediaEnabled,
                BillingStatus = i.Billing != null ? i.Billing.BillingStatus : (BillingStatus?)null,
                BillingExempt = i.Billing != null && i.Billing.BillingExempt
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    private sealed record LeadStats(
        int Total,
        int Last24h,
        int Last7d,
        int AgedCount,
        long? OldestAgeSeconds,
        IReadOnlyList<string> AgedIds,
        IReadOnlyList<(string Tier, int Count)> MailingListByTier);

    /// <summary>
    /// The contact-submission read. Until this endpoint there was none: the form
    /// handler writes the row, emails sales@xcord.net and never looks at the
    /// table again, so an enterprise enquiry whose email bounced was invisible.
    ///
    /// Ids and timestamps only. The submitter's name, company and message are
    /// personal data and do not belong in an operations console.
    /// </summary>
    private async Task<LeadStats> LoadLeadsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var dayAgo = now.AddHours(-24);
        var weekAgo = now.AddDays(-7);

        var submissions = dbContext.ContactSubmissions.AsNoTracking();

        var total = await submissions.CountAsync(ct).ConfigureAwait(false);
        var last24h = await submissions.CountAsync(c => c.CreatedAt >= dayAgo, ct).ConfigureAwait(false);
        var last7d = await submissions.CountAsync(c => c.CreatedAt >= weekAgo, ct).ConfigureAwait(false);

        var aged = await submissions
            .Where(c => c.CreatedAt < dayAgo)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new { c.Id, c.CreatedAt })
            .Take(MaxRows)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var agedCount = total - last24h;

        var byTier = await dbContext.MailingListEntries
            .AsNoTracking()
            .GroupBy(m => m.Tier)
            .Select(g => new { Tier = g.Key, Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new LeadStats(
            total,
            last24h,
            last7d,
            agedCount,
            aged.Count > 0 ? (long)(now - aged[0].CreatedAt).TotalSeconds : null,
            aged.Select(a => a.Id.ToString(CultureInfo.InvariantCulture)).ToList(),
            byTier.OrderBy(t => t.Tier, StringComparer.Ordinal).Select(t => (t.Tier, t.Count)).ToList());
    }

    private sealed record FailedUpgrade(string Domain, string TargetImage, string? ErrorMessage, DateTimeOffset? At);

    private sealed record UpgradeStats(
        IReadOnlyList<FailedUpgrade> Failed,
        int Completed7d,
        int InFlight,
        int RolledBack7d);

    private async Task<UpgradeStats> LoadUpgradesAsync(DateTimeOffset now, CancellationToken ct)
    {
        var weekAgo = now.AddDays(-7);
        var events = dbContext.UpgradeEvents.AsNoTracking();

        var failed = await events
            .Where(u => u.Status == UpgradeEventStatus.Failed && (u.CompletedAt ?? u.StartedAt) >= weekAgo)
            .OrderByDescending(u => u.CompletedAt ?? u.StartedAt)
            .Select(u => new FailedUpgrade(
                u.ManagedInstance.Domain, u.TargetImage, u.ErrorMessage, u.CompletedAt ?? u.StartedAt))
            .Take(MaxRows)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var completed = await events
            .CountAsync(u => u.Status == UpgradeEventStatus.Completed && (u.CompletedAt ?? u.StartedAt) >= weekAgo, ct)
            .ConfigureAwait(false);

        var rolledBack = await events
            .CountAsync(u => u.Status == UpgradeEventStatus.RolledBack && (u.CompletedAt ?? u.StartedAt) >= weekAgo, ct)
            .ConfigureAwait(false);

        // Not windowed: an upgrade still Pending or InProgress is a live one
        // whenever it started, and an old one is worse news than a new one.
        var inFlight = await events
            .CountAsync(u => u.Status == UpgradeEventStatus.Pending || u.Status == UpgradeEventStatus.InProgress, ct)
            .ConfigureAwait(false);

        return new UpgradeStats(failed, completed, inFlight, rolledBack);
    }

    private sealed record UptimeStats(int UnreportedIntervals, long UnreportedMinutes, long? OldestAgeSeconds);

    private async Task<UptimeStats> LoadUptimeAsync(CancellationToken ct)
    {
        // Closed intervals only, matching what ReportUsageToStripeService would
        // pick up. One row per healthy/unhealthy transition, so this is a small
        // read on the (ReportedToStripe, EndedAt) index.
        var intervals = await dbContext.UptimeIntervals
            .AsNoTracking()
            .Where(u => !u.ReportedToStripe && u.EndedAt != null)
            .Select(u => new { u.StartedAt, u.EndedAt })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (intervals.Count == 0)
            return new UptimeStats(0, 0, null);

        var minutes = intervals.Sum(i => (i.EndedAt!.Value - i.StartedAt).TotalMinutes);
        var oldest = intervals.Min(i => i.StartedAt);

        return new UptimeStats(
            intervals.Count,
            (long)Math.Floor(minutes),
            (long)(DateTimeOffset.UtcNow - oldest).TotalSeconds);
    }

    // ======================================================================
    // sections
    // ======================================================================

    private static StatsSection InstancesSection(IReadOnlyList<InstanceStatsRow> instances, string? error)
    {
        if (error is not null)
            return new StatsSection { Title = "Instances", Error = error };

        var rows = new List<IReadOnlyList<object?>>();

        foreach (var status in Enum.GetValues<InstanceStatus>())
        {
            var count = instances.Count(i => i.Status == status);
            if (count == 0 && status is not (InstanceStatus.Running or InstanceStatus.Failed))
                continue;

            rows.Add([
                status.ToString(),
                new StatsCell
                {
                    V = count,
                    Unit = StatsUnit.Count,
                    State = count > 0 && status is InstanceStatus.Failed
                        ? StatsSeverity.Crit
                        : count > 0 && status is InstanceStatus.Suspended or InstanceStatus.Pending
                            ? StatsSeverity.Warn
                            : null
                }
            ]);
        }

        var unhealthy = instances.Where(i => i.Status == InstanceStatus.Running && i.IsHealthy == false).ToList();
        rows.Add([
            "Running but unhealthy",
            new StatsCell
            {
                V = unhealthy.Count,
                Unit = StatsUnit.Count,
                State = unhealthy.Count > 0 ? StatsSeverity.Crit : StatsSeverity.Ok
            }
        ]);

        // Max() on an empty fleet throws, and an empty fleet is the ordinary
        // state of a hub nobody has provisioned an instance on yet.
        var worst = instances.Count == 0 ? 0 : instances.Max(i => i.ConsecutiveFailures ?? 0);
        rows.Add([
            "Worst consecutive failures",
            new StatsCell
            {
                V = worst,
                Unit = StatsUnit.Count,
                // 3 restarts the container, 5 raises the alarm. Anything at or
                // past 5 has already tried both.
                State = worst >= 5 ? StatsSeverity.Crit : worst >= 3 ? StatsSeverity.Warn : StatsSeverity.Ok
            }
        ]);

        rows.Add([
            "Never health-checked",
            new StatsCell { V = instances.Count(i => i.IsHealthy == null), Unit = StatsUnit.Count }
        ]);
        rows.Add([
            "Enterprise tier",
            new StatsCell { V = instances.Count(i => i.Tier == InstanceTier.Enterprise), Unit = StatsUnit.Count }
        ]);

        return new StatsSection
        {
            Title = "Instances",
            Columns = ["Status", "Count"],
            Rows = rows
        };
    }

    private async Task<StatsSection> ProvisioningSectionAsync(DateTimeOffset now, CancellationToken ct)
    {
        var weekAgo = now.AddDays(-7);
        var events = dbContext.ProvisioningEvents.AsNoTracking().Where(e => e.StartedAt >= weekAgo);

        // An instance is "started" once, however many steps it ran.
        var started = await events.Select(e => e.ManagedInstanceId).Distinct().CountAsync(ct).ConfigureAwait(false);
        var completed = await dbContext.ManagedInstances
            .AsNoTracking()
            .CountAsync(i => i.CreatedAt >= weekAgo && i.Status == InstanceStatus.Running, ct)
            .ConfigureAwait(false);

        var failures = await events
            .Where(e => e.Status == ProvisioningStepStatus.Failed)
            .OrderByDescending(e => e.Id)
            .Select(e => new
            {
                Domain = e.ManagedInstance.Domain,
                e.StepName,
                e.Phase,
                e.ErrorMessage,
                At = e.CompletedAt ?? e.StartedAt
            })
            .Take(MaxRows)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var failedCount = await events
            .CountAsync(e => e.Status == ProvisioningStepStatus.Failed, ct)
            .ConfigureAwait(false);

        List<IReadOnlyList<object?>> rows =
        [
            ["Instances started", new StatsCell { V = started, Unit = StatsUnit.Count }],
            ["Reached Running", new StatsCell { V = completed, Unit = StatsUnit.Count }],
            ["Failed steps", new StatsCell
            {
                V = failedCount,
                Unit = StatsUnit.Count,
                State = failedCount > 0 ? StatsSeverity.Warn : StatsSeverity.Ok
            }]
        ];

        foreach (var failure in failures)
        {
            rows.Add([
                failure.Domain,
                failure.StepName,
                failure.Phase.ToString(),
                new StatsCell
                {
                    V = AppStatsAlerts.Cap(failure.ErrorMessage ?? "no error recorded", 160),
                    State = StatsSeverity.Warn
                },
                failure.At.HasValue
                    ? new StatsCell { V = (long)(now - failure.At.Value).TotalSeconds, Unit = StatsUnit.Seconds }
                    : StatsCell.NotTracked(StatsUnit.Seconds)
            ]);
        }

        return new StatsSection
        {
            Title = "Provisioning · 7d",
            Columns = ["Instance / metric", "Step", "Phase", "Error", "Age"],
            Rows = rows,
            Truncated = failedCount > failures.Count ? true : null
        };
    }

    private static StatsSection LeadsSection(LeadStats? leads, string? error)
    {
        if (leads is null)
            return new StatsSection { Title = "Leads", Error = error ?? "not computed" };

        List<IReadOnlyList<object?>> rows =
        [
            ["Submissions · 24h", new StatsCell { V = leads.Last24h, Unit = StatsUnit.Count }],
            ["Submissions · 7d", new StatsCell { V = leads.Last7d, Unit = StatsUnit.Count }],
            ["Older than 24h", new StatsCell
            {
                V = leads.AgedCount,
                Unit = StatsUnit.Count,
                State = leads.AgedCount > 0 ? StatsSeverity.Warn : StatsSeverity.Ok
            }],
            ["Oldest unanswered", leads.OldestAgeSeconds.HasValue
                ? new StatsCell
                {
                    V = leads.OldestAgeSeconds.Value,
                    Unit = StatsUnit.Seconds,
                    State = StatsSeverity.Warn
                }
                : StatsCell.NotTracked(StatsUnit.Seconds)]
        ];

        foreach (var (tier, count) in leads.MailingListByTier)
        {
            rows.Add([
                $"Mailing list · {(string.IsNullOrWhiteSpace(tier) ? "no tier" : tier)}",
                new StatsCell { V = count, Unit = StatsUnit.Count }
            ]);
        }

        return new StatsSection
        {
            Title = "Leads",
            Columns = ["Metric", "Count"],
            Rows = rows
        };
    }

    private async Task<StatsSection> AuthSectionAsync(DateTimeOffset now, CancellationToken ct)
    {
        var dayAgo = now.AddHours(-24);

        var registrations = await dbContext.HubUsers
            .AsNoTracking()
            .CountAsync(u => u.CreatedAt >= dayAgo, ct)
            .ConfigureAwait(false);

        var attempts = dbContext.LoginAttempts.AsNoTracking().Where(a => a.CreatedAt >= dayAgo);
        var succeeded = await attempts.CountAsync(a => a.Success, ct).ConfigureAwait(false);
        var failedAttempts = attempts.Where(a => !a.Success);
        var failed = await failedAttempts.CountAsync(ct).ConfigureAwait(false);
        var failureIps = await failedAttempts
            .Select(a => a.IpAddress)
            .Distinct()
            .CountAsync(ct)
            .ConfigureAwait(false);

        // A hundred failures from one address is a script; from ninety addresses
        // it is credential stuffing, and the two want different responses. The
        // COUNT of distinct addresses says which without putting an address -
        // which is personal data - in the payload.
        var perIp = failureIps > 0 ? (double)failed / failureIps : 0;

        return new StatsSection
        {
            Title = "Auth · 24h",
            Columns = ["Metric", "Count"],
            Rows =
            [
                ["Registrations", new StatsCell { V = registrations, Unit = StatsUnit.Count }],
                ["Logins succeeded", new StatsCell { V = succeeded, Unit = StatsUnit.Count }],
                ["Logins failed", new StatsCell
                {
                    V = failed,
                    Unit = StatsUnit.Count,
                    State = failed >= 50 ? StatsSeverity.Warn : null
                }],
                ["Distinct IPs on failures", new StatsCell
                {
                    V = failureIps,
                    Unit = StatsUnit.Count,
                    // Many addresses AND many failures is the stuffing shape.
                    State = failureIps >= 20 && failed >= 50 ? StatsSeverity.Warn : null
                }],
                ["Failures per IP", new StatsCell
                {
                    V = Math.Round(perIp, 1),
                    State = perIp >= 20 ? StatsSeverity.Warn : null
                }]
            ]
        };
    }

    private async Task<StatsSection> BackupsSectionAsync(DateTimeOffset now, CancellationToken ct)
    {
        var weekAgo = now.AddDays(-7);
        var records = dbContext.BackupRecords.AsNoTracking().Where(b => b.StartedAt >= weekAgo);

        var completed = await records.CountAsync(b => b.Status == BackupStatus.Completed, ct).ConfigureAwait(false);
        var failed = await records.CountAsync(b => b.Status == BackupStatus.Failed, ct).ConfigureAwait(false);
        var inProgress = await records.CountAsync(b => b.Status == BackupStatus.InProgress, ct).ConfigureAwait(false);
        var bytes = await records
            .Where(b => b.Status == BackupStatus.Completed)
            .SumAsync(b => b.SizeBytes, ct)
            .ConfigureAwait(false);

        var policies = await dbContext.BackupPolicies.AsNoTracking().CountAsync(ct).ConfigureAwait(false);

        return new StatsSection
        {
            Title = "Tenant backups · 7d",
            Columns = ["Metric", "Value"],
            Rows =
            [
                ["Completed", new StatsCell { V = completed, Unit = StatsUnit.Count }],
                ["Failed", new StatsCell
                {
                    V = failed,
                    Unit = StatsUnit.Count,
                    State = failed > 0 ? StatsSeverity.Warn : StatsSeverity.Ok
                }],
                ["In progress", new StatsCell { V = inProgress, Unit = StatsUnit.Count }],
                ["Bytes written", new StatsCell { V = bytes, Unit = StatsUnit.Bytes }],
                ["Instances with a policy", new StatsCell { V = policies, Unit = StatsUnit.Count }]
            ]
        };
    }

    private static StatsSection UpgradesSection(
        UpgradeStats? upgrades, string? upgradesError, UptimeStats? uptime, string? uptimeError)
    {
        if (upgrades is null)
            return new StatsSection { Title = "Upgrades", Error = upgradesError ?? "not computed" };

        List<IReadOnlyList<object?>> rows =
        [
            ["Completed · 7d", new StatsCell { V = upgrades.Completed7d, Unit = StatsUnit.Count }],
            ["Failed · 7d", new StatsCell
            {
                V = upgrades.Failed.Count,
                Unit = StatsUnit.Count,
                State = upgrades.Failed.Count > 0 ? StatsSeverity.Warn : StatsSeverity.Ok
            }],
            ["Rolled back · 7d", new StatsCell { V = upgrades.RolledBack7d, Unit = StatsUnit.Count }],
            ["Pending or in progress", new StatsCell
            {
                V = upgrades.InFlight,
                Unit = StatsUnit.Count,
                State = upgrades.InFlight > 0 ? StatsSeverity.Warn : null
            }],
            // Not an upgrade figure, but it belongs beside one: both are things
            // the background services should have finished and have not.
            ["Unreported uptime intervals", uptimeError is null && uptime is not null
                ? new StatsCell
                {
                    V = uptime.UnreportedIntervals,
                    Unit = StatsUnit.Count,
                    State = uptime.UnreportedIntervals > 0 ? StatsSeverity.Warn : StatsSeverity.Ok
                }
                : StatsCell.NotTracked(StatsUnit.Count)]
        ];

        foreach (var failure in upgrades.Failed)
        {
            rows.Add([
                failure.Domain,
                new StatsCell
                {
                    V = AppStatsAlerts.Cap($"{failure.TargetImage}: {failure.ErrorMessage ?? "no error recorded"}", 160),
                    State = StatsSeverity.Warn
                }
            ]);
        }

        return new StatsSection
        {
            Title = "Upgrades",
            Columns = ["Metric", "Value"],
            Rows = rows
        };
    }

    private async Task<StatsSection> CapacitySectionAsync(
        IReadOnlyList<InstanceStatsRow> instances, CancellationToken ct)
    {
        // Every row here consumes a slot, tombstoned or not: worker IDs are never
        // recycled, because reusing one risks a Snowflake collision with the
        // destroyed instance's historical IDs.
        var workerIdsUsed = await dbContext.WorkerIdRegistry
            .AsNoTracking()
            .CountAsync(ct)
            .ConfigureAwait(false);

        var databaseBytes = await dbContext.Database
            .SqlQueryRaw<long>("SELECT pg_database_size(current_database()) AS \"Value\"")
            .SingleAsync(ct)
            .ConfigureAwait(false);

        long? diskFree = null;
        try
        {
            diskFree = new DriveInfo("/").AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read free space for /");
        }

        var used = (double)workerIdsUsed / WorkerIdCapacity;

        return new StatsSection
        {
            Title = "Capacity",
            Columns = ["Resource", "Value"],
            Rows =
            [
                ["Worker IDs used", new StatsCell
                {
                    V = workerIdsUsed,
                    Unit = StatsUnit.Count,
                    State = used >= 0.9 ? StatsSeverity.Crit : used >= 0.75 ? StatsSeverity.Warn : StatsSeverity.Ok
                }],
                ["Worker IDs available", new StatsCell
                {
                    V = WorkerIdCapacity - workerIdsUsed,
                    Unit = StatsUnit.Count
                }],
                ["Instances (not destroyed)", new StatsCell { V = instances.Count, Unit = StatsUnit.Count }],
                ["Hub database", new StatsCell { V = databaseBytes, Unit = StatsUnit.Bytes }],
                // The registry is a sibling container on the guest's bridge with
                // no API the hub is authenticated for. null is the contract's
                // "not tracked" - 0 here would read as an empty registry.
                ["Container registry", StatsCell.NotTracked(StatsUnit.Bytes)],
                ["Disk free", diskFree.HasValue
                    ? new StatsCell
                    {
                        V = diskFree.Value,
                        Unit = StatsUnit.Bytes,
                        State = diskFree.Value < 5L * 1024 * 1024 * 1024 ? StatsSeverity.Warn : StatsSeverity.Ok
                    }
                    : StatsCell.NotTracked(StatsUnit.Bytes)]
            ]
        };
    }

    // ======================================================================
    // helpers
    // ======================================================================

    /// <summary>
    /// What the fleet bills per month, right now, in cents.
    ///
    /// Flat-rate tiers only: an Enterprise instance on metered billing has no
    /// monthly price, its revenue is the uptime it accrues, and counting it at
    /// the flat Enterprise rate would invent money. Suspended, exempt and
    /// unpaid instances are not earning either.
    /// </summary>
    private static long MonthlyRecurringCents(IReadOnlyList<InstanceStatsRow> instances) =>
        instances
            .Where(i => i.Tier.HasValue &&
                        !i.BillingExempt &&
                        i.BillingStatus == Entities.BillingStatus.Active &&
                        i.Status is InstanceStatus.Running or InstanceStatus.Upgrading)
            .Sum(i => (long)TierDefaults.GetTotalPriceCents(i.Tier!.Value, i.MediaEnabled));

    private StatsSection SafeSection(string title, Func<StatsSection> build)
    {
        try
        {
            return build();
        }
        catch (Exception ex)
        {
            return new StatsSection { Title = title, Error = Describe(ex, title) };
        }
    }

    private async Task<StatsSection> SafeSectionAsync(
        string title, Func<CancellationToken, Task<StatsSection>> build, CancellationToken ct)
    {
        try
        {
            return await build(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (NotCancellation(ex, ct))
        {
            return new StatsSection { Title = title, Error = Describe(ex, title) };
        }
    }

    /// <summary>
    /// A cancelled request is the caller giving up, not a section that failed;
    /// letting it through would report "query timed out" for a console tab that
    /// was simply closed.
    /// </summary>
    private static bool NotCancellation(Exception ex, CancellationToken ct) =>
        ex is not OperationCanceledException || !ct.IsCancellationRequested;

    private string Describe(Exception ex, string what)
    {
        logger.LogWarning(ex, "Stats: {What} could not be computed", what);
        return AppStatsAlerts.Cap($"{ex.GetType().Name}: {ex.Message}", 300);
    }

    private static string Iso(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string? HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? $"{parsed.Scheme}://{parsed.Authority}" : "set";

    /// <summary>
    /// The deployed build, so it can be lined up against the deploy panel's
    /// digest. Set by the Dockerfile's -p:Version; "0.0.0-dev" outside a build.
    /// </summary>
    private static string? DeployedVersion() =>
        typeof(GetAppStatsHandler).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        // The same constant the rate limiter matches on, so the route and its
        // limit cannot drift apart.
        return app.MapGet(StatsAccessGuard.RoutePath, async (
            HttpContext http,
            GetAppStatsHandler handler,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            // Deliberately NOT RequireAuthorization(Policies.Admin): this is the
            // machine door, and the JWT gate on every other admin route is
            // untouched. See StatsAccessGuard for why it is three checks.
            var expected = configuration[StatsAccessGuard.TokenConfigurationKey];
            switch (StatsAccessGuard.Evaluate(http, expected))
            {
                case StatsAccessGuard.Decision.NotOnTailnet:
                    return Results.StatusCode(StatusCodes.Status403Forbidden);

                // 503, never 200. An unset token must not open the endpoint, and
                // the console tells "misconfigured" apart from "unauthorized".
                case StatsAccessGuard.Decision.TokenNotConfigured:
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

                case StatsAccessGuard.Decision.BadToken:
                    return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            var result = await handler.Handle(new GetAppStatsQuery(), ct).ConfigureAwait(false);

            // Results.Json with the contract's own options: the hub's global
            // serializer is camelCase and writes every long as a string.
            return result.Match(
                payload => Results.Json(payload, StatsJson.Options),
                error => Results.Problem(statusCode: error.StatusCode, title: error.Code, detail: error.Message));
        })
        // AllowAnonymous, because the guard above is the authentication for
        // this one route. Rate limiting is applied by the global limiter, which
        // matches on the path - see StatsAccessGuard.RoutePath for why it cannot
        // be a RequireRateLimiting policy here.
        .AllowAnonymous()
        // No .Produces<AppStatsResponse>: the OpenAPI document is generated from
        // the C# shape under the hub's GLOBAL camelCase policy, and this response
        // is snake_case by contract - publishing it would put `generatedAt` and
        // `maxAgeSeconds` into src/frontend/generated/api-types.ts for a payload
        // whose every field is spelled differently on the wire. The contract
        // lives in contracts/app-stats.schema.json, and that is the schema this
        // endpoint answers to.
        .WithName("GetAppStats")
        .WithTags("Admin");
    }
}
