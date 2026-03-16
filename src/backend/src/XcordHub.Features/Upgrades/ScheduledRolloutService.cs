using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Upgrades;

public sealed class ScheduledRolloutService(
    IServiceScopeFactory serviceScopeFactory,
    IUpgradeQueue upgradeQueue,
    ILogger<ScheduledRolloutService> logger) : PollingBackgroundService(serviceScopeFactory, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromSeconds(60);

    protected override async Task ProcessAsync(CancellationToken ct)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HubDbContext>();

        var now = DateTimeOffset.UtcNow;

        var dueRollouts = await dbContext.UpgradeRollouts
            .Where(r => r.Status == RolloutStatus.Pending
                && r.ScheduledAt != null
                && r.ScheduledAt <= now)
            .ToListAsync(ct);

        foreach (var rollout in dueRollouts)
        {
            // Atomically set to InProgress before enqueue to prevent double-enqueue
            rollout.Status = RolloutStatus.InProgress;
            await dbContext.SaveChangesAsync(ct);

            Logger.LogInformation("Scheduled rollout {RolloutId} is due (scheduled for {ScheduledAt}), enqueuing",
                rollout.Id, rollout.ScheduledAt);

            await upgradeQueue.EnqueueRolloutAsync(rollout.Id, force: false, ct);
        }
    }
}
