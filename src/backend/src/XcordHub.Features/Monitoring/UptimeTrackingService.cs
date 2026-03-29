using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Monitoring;

/// <summary>
/// Background service that converts health check state transitions into uptime intervals.
/// Runs after each health check cycle and manages open/closed intervals per instance.
/// Only creates intervals for Enterprise instances (both flat and metered billing types).
/// </summary>
public sealed class UptimeTrackingService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<UptimeTrackingService> logger) : PollingBackgroundService(serviceScopeFactory, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromSeconds(90);

    protected override async Task ProcessAsync(CancellationToken ct)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HubDbContext>();

        // Get all Enterprise instances with their health and billing records
        var instances = await dbContext.ManagedInstances
            .Include(i => i.Health)
            .Include(i => i.Billing)
            .Where(i =>
                i.DeletedAt == null &&
                i.Status == InstanceStatus.Running &&
                i.Billing != null &&
                i.Billing.Tier == InstanceTier.Enterprise)
            .ToListAsync(ct);

        Logger.LogDebug("UptimeTrackingService: processing {Count} Enterprise instances", instances.Count);

        foreach (var instance in instances)
        {
            if (instance.Health == null || instance.Billing == null)
                continue;

            await TrackInstanceUptimeAsync(instance, dbContext, ct);
        }
    }

    private async Task TrackInstanceUptimeAsync(
        ManagedInstance instance,
        HubDbContext dbContext,
        CancellationToken ct)
    {
        var isHealthy = instance.Health!.IsHealthy;
        var now = DateTimeOffset.UtcNow;

        // Find the most recent open interval (EndedAt IS NULL) for this instance
        var openInterval = await dbContext.UptimeIntervals
            .Where(u => u.ManagedInstanceId == instance.Id && u.EndedAt == null)
            .OrderByDescending(u => u.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (isHealthy)
        {
            // Instance is healthy - open a new interval if none is open
            if (openInterval == null)
            {
                var newInterval = new UptimeInterval
                {
                    ManagedInstanceId = instance.Id,
                    StartedAt = instance.Health.LastCheckAt,
                    EndedAt = null,
                    ReportedToStripe = false,
                    CreatedAt = now
                };
                dbContext.UptimeIntervals.Add(newInterval);
                await dbContext.SaveChangesAsync(ct);

                Logger.LogInformation(
                    "Opened uptime interval for Enterprise instance {InstanceId} ({Domain}) at {StartedAt}",
                    instance.Id, instance.Domain, newInterval.StartedAt);
            }
            // else: interval is already open, nothing to do
        }
        else
        {
            // Instance is unhealthy - close any open interval
            if (openInterval != null)
            {
                openInterval.EndedAt = instance.Health.LastCheckAt;
                await dbContext.SaveChangesAsync(ct);

                Logger.LogInformation(
                    "Closed uptime interval {IntervalId} for Enterprise instance {InstanceId} ({Domain}). Duration: {Minutes:F1} minutes",
                    openInterval.Id, instance.Id, instance.Domain,
                    openInterval.DurationMinutes ?? 0);
            }
        }
    }
}
