using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Upgrades;

public sealed class UpgradeBackgroundService : BackgroundService
{
    private readonly IUpgradeQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UpgradeBackgroundService> _logger;

    public UpgradeBackgroundService(
        IUpgradeQueue queue,
        IServiceProvider serviceProvider,
        ILogger<UpgradeBackgroundService> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Upgrade background service started");

        await RecoverStuckUpgradesAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var workItem = await _queue.DequeueAsync(stoppingToken);

                using var scope = _serviceProvider.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<UpgradeOrchestrator>();

                if (workItem.InstanceUpgrade is { } instanceUpgrade)
                {
                    _logger.LogInformation("Processing instance upgrade for {InstanceId} to {Image}",
                        instanceUpgrade.InstanceId, instanceUpgrade.TargetImage);

                    var result = await orchestrator.UpgradeInstanceAsync(
                        instanceUpgrade.InstanceId,
                        instanceUpgrade.TargetImage,
                        instanceUpgrade.RolloutId,
                        stoppingToken);

                    if (result.IsFailure)
                    {
                        _logger.LogError("Instance upgrade failed for {InstanceId}: {Error}",
                            instanceUpgrade.InstanceId, result.Error?.Message);
                    }
                }
                else if (workItem.Rollout is { } rollout)
                {
                    _logger.LogInformation("Processing rollout {RolloutId}", rollout.RolloutId);

                    var result = await orchestrator.ExecuteRolloutAsync(
                        rollout.RolloutId,
                        rollout.Force,
                        stoppingToken);

                    if (result.IsFailure)
                    {
                        _logger.LogError("Rollout {RolloutId} failed: {Error}",
                            rollout.RolloutId, result.Error?.Message);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing upgrade work item");
            }
        }

        _logger.LogInformation("Upgrade background service stopped");
    }

    private async Task RecoverStuckUpgradesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<HubDbContext>();

            // Recover instances stuck in Upgrading state
            var stuckInstances = await dbContext.ManagedInstances
                .Where(i => i.Status == InstanceStatus.Upgrading && i.DeletedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var instance in stuckInstances)
            {
                _logger.LogWarning("Recovering stuck upgrading instance {InstanceId} — marking as Failed", instance.Id);
                instance.Status = InstanceStatus.Failed;
            }

            // Recover rollouts stuck in InProgress state
            var stuckRollouts = await dbContext.UpgradeRollouts
                .Where(r => r.Status == RolloutStatus.InProgress)
                .ToListAsync(cancellationToken);

            foreach (var rollout in stuckRollouts)
            {
                _logger.LogWarning("Recovering stuck in-progress rollout {RolloutId} — marking as Failed", rollout.Id);
                rollout.Status = RolloutStatus.Failed;
                rollout.ErrorMessage = "Hub restarted during rollout";
                rollout.CompletedAt = DateTimeOffset.UtcNow;
            }

            if (stuckInstances.Count > 0 || stuckRollouts.Count > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Recovered {InstanceCount} stuck instances and {RolloutCount} stuck rollouts",
                    stuckInstances.Count, stuckRollouts.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recovering stuck upgrades on startup");
        }
    }
}
