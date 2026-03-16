using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Upgrades;

public sealed class MinimumVersionEnforcerService(
    IServiceScopeFactory serviceScopeFactory,
    IUpgradeQueue upgradeQueue,
    ILogger<MinimumVersionEnforcerService> logger) : PollingBackgroundService(serviceScopeFactory, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task ProcessAsync(CancellationToken ct)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HubDbContext>();

        var now = DateTimeOffset.UtcNow;

        // Find versions that have reached their minimum enforcement date
        var enforcedVersions = await dbContext.AvailableVersions
            .Where(v => v.IsMinimumVersion
                && v.MinimumEnforcementDate != null
                && v.MinimumEnforcementDate <= now
                && v.DeletedAt == null)
            .ToListAsync(ct);

        if (enforcedVersions.Count == 0)
        {
            return;
        }

        Logger.LogInformation(
            "Checking {Count} enforced minimum version(s)",
            enforcedVersions.Count);

        foreach (var enforcedVersion in enforcedVersions)
        {
            // Find Running instances not on the enforced image, with batch upgrades disabled
            var nonCompliantInstances = await dbContext.ManagedInstances
                .Include(i => i.Infrastructure)
                .Include(i => i.Config)
                .Where(i => i.Status == InstanceStatus.Running
                    && i.DeletedAt == null
                    && i.Infrastructure != null
                    && i.Infrastructure.DeployedImage != enforcedVersion.Image
                    && i.Config != null
                    && !i.Config.BatchUpgradesEnabled)
                .ToListAsync(ct);

            if (nonCompliantInstances.Count == 0)
            {
                continue;
            }

            Logger.LogWarning(
                "Enforcing minimum version {Version} ({Image}) on {Count} non-compliant instance(s)",
                enforcedVersion.Version, enforcedVersion.Image, nonCompliantInstances.Count);

            foreach (var instance in nonCompliantInstances)
            {
                Logger.LogWarning(
                    "Force upgrading instance {InstanceId} ({Domain}) from {CurrentImage} to minimum version {Version} ({TargetImage})",
                    instance.Id,
                    instance.Domain,
                    instance.Infrastructure!.DeployedImage,
                    enforcedVersion.Version,
                    enforcedVersion.Image);

                await upgradeQueue.EnqueueInstanceUpgradeAsync(
                    instance.Id,
                    enforcedVersion.Image,
                    cancellationToken: ct);
            }
        }
    }
}
