using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Upgrades;

public sealed class UpgradeOrchestrator
{
    private readonly HubDbContext _dbContext;
    private readonly IDockerService _dockerService;
    private readonly IInstanceNotifier _instanceNotifier;
    private readonly IHealthCheckVerifier _healthCheckVerifier;
    private readonly ILogger<UpgradeOrchestrator> _logger;

    private static readonly TimeSpan ContainerPollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ContainerPollTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan HealthPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HealthPollTimeout = TimeSpan.FromSeconds(60);

    public UpgradeOrchestrator(
        HubDbContext dbContext,
        IDockerService dockerService,
        IInstanceNotifier instanceNotifier,
        IHealthCheckVerifier healthCheckVerifier,
        ILogger<UpgradeOrchestrator> logger)
    {
        _dbContext = dbContext;
        _dockerService = dockerService;
        _instanceNotifier = instanceNotifier;
        _healthCheckVerifier = healthCheckVerifier;
        _logger = logger;
    }

    public async Task<Result<bool>> UpgradeInstanceAsync(
        long instanceId,
        string targetImage,
        long? rolloutId = null,
        CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.ManagedInstances
            .Include(i => i.Infrastructure)
            .Include(i => i.Config)
            .Include(i => i.Health)
            .FirstOrDefaultAsync(i => i.Id == instanceId && i.DeletedAt == null, cancellationToken);

        if (instance == null)
            return Error.NotFound("INSTANCE_NOT_FOUND", $"Instance {instanceId} not found");

        if (instance.Status != InstanceStatus.Running && instance.Status != InstanceStatus.Failed)
            return Error.Failure("INSTANCE_NOT_UPGRADEABLE", $"Instance {instanceId} has status {instance.Status}");

        if (instance.Infrastructure == null)
            return Error.Failure("INFRASTRUCTURE_NOT_FOUND", $"Infrastructure for instance {instanceId} not found");

        var previousImage = instance.Infrastructure.DeployedImage;
        var previousVersion = instance.Health?.Version;
        var containerId = instance.Infrastructure.DockerContainerId;

        // Set instance to upgrading state
        instance.Status = InstanceStatus.Upgrading;

        var now = DateTimeOffset.UtcNow;
        var upgradeEvent = new UpgradeEvent
        {
            Id = 0, // DB-generated
            UpgradeRolloutId = rolloutId,
            ManagedInstanceId = instanceId,
            Status = UpgradeEventStatus.InProgress,
            PreviousImage = previousImage,
            TargetImage = targetImage,
            PreviousVersion = previousVersion,
            StartedAt = now
        };

        _dbContext.UpgradeEvents.Add(upgradeEvent);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            // Notify instance of upcoming shutdown
            await _instanceNotifier.NotifyShuttingDownAsync(instance.Domain, "upgrade", cancellationToken);

            // Update the service image
            await _dockerService.UpdateServiceImageAsync(containerId, targetImage, cancellationToken);

            // Poll for container to be running (60s timeout, check every 3s)
            var containerRunning = await PollWithTimeoutAsync(
                async ct => await _dockerService.VerifyContainerRunningAsync(containerId, ct),
                ContainerPollTimeout,
                ContainerPollInterval,
                cancellationToken);

            if (!containerRunning)
            {
                return await HandleFailureAsync(instance, upgradeEvent, previousImage, containerId,
                    "Container did not start within timeout", cancellationToken);
            }

            // Poll for health check with new version (60s timeout, check every 5s)
            string? newVersion = null;
            var healthyWithNewVersion = await PollWithTimeoutAsync(
                async ct =>
                {
                    var (isHealthy, _, _, version) = await _healthCheckVerifier.VerifyInstanceHealthAsync(instance.Domain, ct);
                    if (isHealthy && version != null && version != previousVersion)
                    {
                        newVersion = version;
                        return true;
                    }
                    return false;
                },
                HealthPollTimeout,
                HealthPollInterval,
                cancellationToken);

            if (!healthyWithNewVersion)
            {
                // Check if at least healthy (version might not have changed for same-image redeploy)
                var (isHealthy, _, _, version) = await _healthCheckVerifier.VerifyInstanceHealthAsync(instance.Domain, cancellationToken);
                if (isHealthy)
                {
                    newVersion = version;
                    _logger.LogWarning("Instance {InstanceId} is healthy after upgrade but version stayed at {Version} (expected change from {PreviousVersion})",
                        instanceId, version, previousVersion);
                }
                else
                {
                    return await HandleFailureAsync(instance, upgradeEvent, previousImage, containerId,
                        "Instance did not become healthy within timeout", cancellationToken);
                }
            }

            // Success - update records
            instance.Infrastructure.DeployedImage = targetImage;

            if (newVersion != null && instance.Health != null)
                instance.Health.Version = newVersion;

            instance.Status = InstanceStatus.Running;

            upgradeEvent.Status = UpgradeEventStatus.Completed;
            upgradeEvent.NewVersion = newVersion;
            upgradeEvent.CompletedAt = DateTimeOffset.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Instance {InstanceId} upgraded to {TargetImage} (version {Version})",
                instanceId, targetImage, newVersion ?? "unknown");

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Upgrade failed for instance {InstanceId}", instanceId);
            return await HandleFailureAsync(instance, upgradeEvent, previousImage, containerId,
                ex.Message, cancellationToken);
        }
    }

    public async Task<Result<bool>> ExecuteRolloutAsync(
        long rolloutId,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var rollout = await _dbContext.UpgradeRollouts
            .FirstOrDefaultAsync(r => r.Id == rolloutId, cancellationToken);

        if (rollout == null)
            return Error.NotFound("ROLLOUT_NOT_FOUND", $"Rollout {rolloutId} not found");

        if (rollout.Status != RolloutStatus.Pending)
            return Error.Failure("ROLLOUT_ALREADY_PROCESSED", $"Rollout {rolloutId} has status {rollout.Status}");

        // Find target instances: Running instances where DeployedImage != rollout.ToImage
        var query = _dbContext.ManagedInstances
            .Include(i => i.Infrastructure)
            .Include(i => i.Config)
            .Include(i => i.Health)
            .Where(i => i.Status == InstanceStatus.Running
                && i.DeletedAt == null
                && i.Infrastructure != null
                && i.Infrastructure.DeployedImage != rollout.ToImage);

        // Filter by target pool if set
        if (!string.IsNullOrWhiteSpace(rollout.TargetPool))
            query = query.Where(i => i.Infrastructure!.PlacedInPool == rollout.TargetPool);

        // Filter by upgrade policy: skip Manual and Pinned unless force
        if (!force)
            query = query.Where(i => i.Config == null || i.Config.UpgradePolicy == UpgradePolicy.Auto);

        var targetInstances = await query.ToListAsync(cancellationToken);

        rollout.TotalInstances = targetInstances.Count;
        rollout.Status = RolloutStatus.InProgress;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Starting rollout {RolloutId}: {Count} instances to upgrade to {Image}",
            rolloutId, targetInstances.Count, rollout.ToImage);

        // Process instances one at a time (sequential)
        foreach (var instance in targetInstances)
        {
            var result = await UpgradeInstanceAsync(instance.Id, rollout.ToImage, rolloutId, cancellationToken);

            if (result.IsSuccess)
            {
                rollout.CompletedInstances++;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                rollout.FailedInstanceId = instance.Id;
                rollout.ErrorMessage = result.Error?.Message;
                rollout.Status = RolloutStatus.Failed;
                rollout.CompletedAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogError("Rollout {RolloutId} failed at instance {InstanceId}: {Error}",
                    rolloutId, instance.Id, result.Error?.Message);

                return Error.Failure("ROLLOUT_FAILED", $"Rollout failed at instance {instance.Id}: {result.Error?.Message}");
            }
        }

        // All succeeded
        rollout.Status = RolloutStatus.Completed;
        rollout.CompletedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Rollout {RolloutId} completed: {Count} instances upgraded",
            rolloutId, rollout.CompletedInstances);

        return true;
    }

    private async Task<Result<bool>> HandleFailureAsync(
        ManagedInstance instance,
        UpgradeEvent upgradeEvent,
        string? previousImage,
        string containerId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        // Attempt to revert image (swallow errors)
        if (!string.IsNullOrWhiteSpace(previousImage))
        {
            try
            {
                await _dockerService.UpdateServiceImageAsync(containerId, previousImage, cancellationToken);
                _logger.LogInformation("Reverted instance {InstanceId} to previous image {Image}",
                    instance.Id, previousImage);
            }
            catch (Exception revertEx)
            {
                _logger.LogWarning(revertEx, "Failed to revert instance {InstanceId} to previous image {Image}",
                    instance.Id, previousImage);
            }
        }

        instance.Status = InstanceStatus.Failed;

        upgradeEvent.Status = UpgradeEventStatus.Failed;
        upgradeEvent.ErrorMessage = errorMessage;
        upgradeEvent.CompletedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Error.Failure("UPGRADE_FAILED", errorMessage);
    }

    private static async Task<bool> PollWithTimeoutAsync(
        Func<CancellationToken, Task<bool>> check,
        TimeSpan timeout,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                try
                {
                    if (await check(timeoutCts.Token))
                        return true;
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // Swallow transient errors during polling
                }

                await Task.Delay(interval, timeoutCts.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout expired, not external cancellation
        }

        return false;
    }
}
