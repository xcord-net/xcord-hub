using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Upgrades;

public sealed class MinimumVersionEnforcerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IUpgradeQueue _upgradeQueue;
    private readonly ILogger<MinimumVersionEnforcerService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

    public MinimumVersionEnforcerService(
        IServiceProvider serviceProvider,
        IUpgradeQueue upgradeQueue,
        ILogger<MinimumVersionEnforcerService> logger)
    {
        _serviceProvider = serviceProvider;
        _upgradeQueue = upgradeQueue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MinimumVersionEnforcerService started");

        // Wait 30 seconds on startup before first check
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        using var timer = new PeriodicTimer(_checkInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnforceMinimumVersionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enforcing minimum versions");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("MinimumVersionEnforcerService stopped");
    }

    private async Task EnforceMinimumVersionsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HubDbContext>();

        var now = DateTimeOffset.UtcNow;

        // Find versions that have reached their minimum enforcement date
        var enforcedVersions = await dbContext.AvailableVersions
            .Where(v => v.IsMinimumVersion
                && v.MinimumEnforcementDate != null
                && v.MinimumEnforcementDate <= now
                && v.DeletedAt == null)
            .ToListAsync(cancellationToken);

        if (enforcedVersions.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Checking {Count} enforced minimum version(s)",
            enforcedVersions.Count);

        foreach (var enforcedVersion in enforcedVersions)
        {
            // Find Running instances not on the enforced image, with Manual or Pinned upgrade policy
            var nonCompliantInstances = await dbContext.ManagedInstances
                .Include(i => i.Infrastructure)
                .Include(i => i.Config)
                .Where(i => i.Status == InstanceStatus.Running
                    && i.DeletedAt == null
                    && i.Infrastructure != null
                    && i.Infrastructure.DeployedImage != enforcedVersion.Image
                    && i.Config != null
                    && (i.Config.UpgradePolicy == UpgradePolicy.Manual
                        || i.Config.UpgradePolicy == UpgradePolicy.Pinned))
                .ToListAsync(cancellationToken);

            if (nonCompliantInstances.Count == 0)
            {
                continue;
            }

            _logger.LogWarning(
                "Enforcing minimum version {Version} ({Image}) on {Count} non-compliant instance(s)",
                enforcedVersion.Version, enforcedVersion.Image, nonCompliantInstances.Count);

            foreach (var instance in nonCompliantInstances)
            {
                _logger.LogWarning(
                    "Force upgrading instance {InstanceId} ({Domain}) from {CurrentImage} to minimum version {Version} ({TargetImage})",
                    instance.Id,
                    instance.Domain,
                    instance.Infrastructure!.DeployedImage,
                    enforcedVersion.Version,
                    enforcedVersion.Image);

                await _upgradeQueue.EnqueueInstanceUpgradeAsync(
                    instance.Id,
                    enforcedVersion.Image,
                    cancellationToken: cancellationToken);
            }
        }
    }
}
