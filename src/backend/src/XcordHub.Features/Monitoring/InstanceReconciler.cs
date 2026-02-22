using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Monitoring;

public sealed class InstanceReconciler : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InstanceReconciler> _logger;
    private readonly TimeSpan _reconcileInterval = TimeSpan.FromSeconds(60);
    private readonly TimeSpan _provisioningTimeout = TimeSpan.FromMinutes(5);

    public InstanceReconciler(
        IServiceProvider serviceProvider,
        ILogger<InstanceReconciler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InstanceReconciler started");

        // Wait 5 seconds on startup before first reconcile
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        using var timer = new PeriodicTimer(_reconcileInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileInstancesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reconciling instances");
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

        _logger.LogInformation("InstanceReconciler stopped");
    }

    private async Task ReconcileInstancesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var dockerService = scope.ServiceProvider.GetRequiredService<IDockerService>();
        var proxyManager = scope.ServiceProvider.GetRequiredService<ICaddyProxyManager>();
        var healthVerifier = scope.ServiceProvider.GetRequiredService<IHealthCheckVerifier>();
        var provisioningQueue = scope.ServiceProvider.GetRequiredService<IProvisioningQueue>();

        // 1. Reconcile Running instances - verify all resources exist
        await ReconcileRunningInstancesAsync(
            dbContext,
            dockerService,
            proxyManager,
            healthVerifier,
            cancellationToken);

        // 2. Detect stuck Provisioning instances
        await DetectStuckProvisioningAsync(
            dbContext,
            provisioningQueue,
            cancellationToken);

        _logger.LogInformation("Instance reconciliation completed");
    }

    private async Task ReconcileRunningInstancesAsync(
        HubDbContext dbContext,
        IDockerService dockerService,
        ICaddyProxyManager proxyManager,
        IHealthCheckVerifier healthVerifier,
        CancellationToken cancellationToken)
    {
        var runningInstances = await dbContext.ManagedInstances
            .Include(i => i.Infrastructure)
            .Where(i => i.Status == InstanceStatus.Running && i.DeletedAt == null)
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Reconciling {Count} running instances", runningInstances.Count);

        foreach (var instance in runningInstances)
        {
            if (instance.Infrastructure == null)
            {
                _logger.LogWarning(
                    "Running instance {InstanceId} has no infrastructure, marking as Failed",
                    instance.Id);

                instance.Status = InstanceStatus.Failed;
                await dbContext.SaveChangesAsync(cancellationToken);
                continue;
            }

            await ReconcileInstanceResourcesAsync(
                instance,
                dockerService,
                proxyManager,
                healthVerifier,
                dbContext,
                cancellationToken);
        }
    }

    private async Task ReconcileInstanceResourcesAsync(
        Entities.ManagedInstance instance,
        IDockerService dockerService,
        ICaddyProxyManager proxyManager,
        IHealthCheckVerifier healthVerifier,
        HubDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var infrastructure = instance.Infrastructure!;
        var hasIssues = false;
        var issueDetails = new List<string>();

        try
        {
            // 1. Verify Docker network exists
            var networkExists = await dockerService.VerifyNetworkAsync(
                infrastructure.DockerNetworkId,
                cancellationToken);

            if (!networkExists)
            {
                _logger.LogWarning(
                    "Instance {InstanceId} ({Domain}) network missing",
                    instance.Id, instance.Domain);
                hasIssues = true;
                issueDetails.Add("Network missing");
            }

            // 2. Verify container is running
            var containerRunning = await dockerService.VerifyContainerRunningAsync(
                infrastructure.DockerContainerId,
                cancellationToken);

            if (!containerRunning)
            {
                _logger.LogWarning(
                    "Instance {InstanceId} ({Domain}) container not running",
                    instance.Id, instance.Domain);
                hasIssues = true;
                issueDetails.Add("Container not running");
            }

            // 3. Verify proxy route exists
            var routeExists = await proxyManager.VerifyRouteAsync(
                infrastructure.CaddyRouteId,
                cancellationToken);

            if (!routeExists)
            {
                _logger.LogWarning(
                    "Instance {InstanceId} ({Domain}) proxy route missing",
                    instance.Id, instance.Domain);
                hasIssues = true;
                issueDetails.Add("Proxy route missing");
            }

            // 4. Verify health endpoint (if container is running)
            if (containerRunning)
            {
                var (isHealthy, _, errorMessage) = await healthVerifier.VerifyInstanceHealthAsync(
                    instance.Domain,
                    cancellationToken);

                if (!isHealthy)
                {
                    _logger.LogWarning(
                        "Instance {InstanceId} ({Domain}) health check failed: {Error}",
                        instance.Id, instance.Domain, errorMessage);
                    hasIssues = true;
                    issueDetails.Add($"Health check failed: {errorMessage}");
                }
            }

            // If critical issues detected, mark as Failed
            if (hasIssues && issueDetails.Any(d => d.Contains("Network") || d.Contains("Container")))
            {
                _logger.LogError(
                    "Instance {InstanceId} ({Domain}) has critical infrastructure issues: {Issues}",
                    instance.Id, instance.Domain, string.Join(", ", issueDetails));

                instance.Status = InstanceStatus.Failed;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error reconciling instance {InstanceId} ({Domain})",
                instance.Id, instance.Domain);
        }
    }

    private async Task DetectStuckProvisioningAsync(
        HubDbContext dbContext,
        IProvisioningQueue provisioningQueue,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var stuckCutoff = now - _provisioningTimeout;

        var stuckInstances = await dbContext.ManagedInstances
            .Where(i => i.Status == InstanceStatus.Provisioning
                && i.CreatedAt < stuckCutoff
                && i.DeletedAt == null)
            .ToListAsync(cancellationToken);

        if (stuckInstances.Count == 0)
        {
            return;
        }

        _logger.LogWarning(
            "Detected {Count} stuck provisioning instances (timeout: {Timeout} minutes)",
            stuckInstances.Count, _provisioningTimeout.TotalMinutes);

        foreach (var instance in stuckInstances)
        {
            _logger.LogError(
                "Instance {InstanceId} ({Domain}) stuck in Provisioning for {Duration} minutes, re-enqueueing",
                instance.Id, instance.Domain, (now - instance.CreatedAt).TotalMinutes);

            // Re-enqueue with backoff
            await provisioningQueue.EnqueueAsync(instance.Id, cancellationToken);

            // Update status to Pending to trigger re-provisioning
            instance.Status = InstanceStatus.Pending;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
