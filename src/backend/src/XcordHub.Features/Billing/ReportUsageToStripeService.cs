using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Billing;

/// <summary>
/// Background service that periodically reports accumulated uptime minutes to Stripe
/// for Enterprise instances on metered (usage-based) billing.
/// Runs hourly. Picks up all closed, unreported uptime intervals, sums their duration,
/// and reports the total as a single usage record per instance.
/// Open intervals (instance still running) are included as a partial record.
/// </summary>
public sealed class ReportUsageToStripeService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<ReportUsageToStripeService> logger) : PollingBackgroundService(serviceScopeFactory, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task ProcessAsync(CancellationToken ct)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var stripeService = scope.ServiceProvider.GetRequiredService<IStripeService>();
        var stripeOptions = scope.ServiceProvider.GetRequiredService<IOptions<StripeOptions>>().Value;

        if (!stripeOptions.IsConfigured)
        {
            Logger.LogDebug("Stripe not configured, skipping usage reporting");
            return;
        }

        // Find all Enterprise metered instances with a subscription item ID
        var meteredInstances = await dbContext.ManagedInstances
            .Include(i => i.Billing)
            .Where(i =>
                i.DeletedAt == null &&
                i.Billing != null &&
                i.Billing.Tier == InstanceTier.Enterprise &&
                i.Billing.IsMeteredBilling &&
                i.Billing.StripeSubscriptionItemId != null)
            .ToListAsync(ct);

        Logger.LogInformation(
            "ReportUsageToStripeService: found {Count} Enterprise metered instances to report",
            meteredInstances.Count);

        var now = DateTimeOffset.UtcNow;

        foreach (var instance in meteredInstances)
        {
            await ReportInstanceUsageAsync(instance, dbContext, stripeService, now, ct);
        }
    }

    private async Task ReportInstanceUsageAsync(
        ManagedInstance instance,
        HubDbContext dbContext,
        IStripeService stripeService,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var billing = instance.Billing!;
        var subItemId = billing.StripeSubscriptionItemId!;

        try
        {
            // Collect all closed, unreported intervals
            var unreportedIntervals = await dbContext.UptimeIntervals
                .Where(u =>
                    u.ManagedInstanceId == instance.Id &&
                    !u.ReportedToStripe &&
                    u.EndedAt != null)
                .ToListAsync(ct);

            // Also include the currently-open interval (partial uptime since last report)
            var openInterval = await dbContext.UptimeIntervals
                .Where(u => u.ManagedInstanceId == instance.Id && u.EndedAt == null)
                .FirstOrDefaultAsync(ct);

            double totalMinutes = unreportedIntervals.Sum(u => u.DurationMinutes ?? 0);

            // Add partial minutes from open interval (up to current time)
            if (openInterval != null)
            {
                totalMinutes += (now - openInterval.StartedAt).TotalMinutes;
            }

            if (totalMinutes < 1)
            {
                Logger.LogDebug(
                    "Instance {InstanceId} has less than 1 minute of unreported uptime, skipping",
                    instance.Id);
                return;
            }

            var minutesToReport = (long)Math.Floor(totalMinutes);

            await stripeService.ReportUsageAsync(subItemId, minutesToReport, now, ct);

            // Mark all closed intervals as reported
            foreach (var interval in unreportedIntervals)
            {
                interval.ReportedToStripe = true;
                interval.ReportedAt = now;
            }

            // If there's an open interval, close it and immediately open a fresh one
            // so the next report cycle doesn't double-count
            if (openInterval != null)
            {
                openInterval.EndedAt = now;
                openInterval.ReportedToStripe = true;
                openInterval.ReportedAt = now;

                // Open a fresh interval starting from now
                var freshInterval = new UptimeInterval
                {
                    ManagedInstanceId = instance.Id,
                    StartedAt = now,
                    EndedAt = null,
                    ReportedToStripe = false,
                    CreatedAt = now
                };
                dbContext.UptimeIntervals.Add(freshInterval);
            }

            await dbContext.SaveChangesAsync(ct);

            Logger.LogInformation(
                "Reported {Minutes} uptime minutes to Stripe for instance {InstanceId} ({Domain})",
                minutesToReport, instance.Id, instance.Domain);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Failed to report usage to Stripe for instance {InstanceId} ({Domain})",
                instance.Id, instance.Domain);
        }
    }
}
