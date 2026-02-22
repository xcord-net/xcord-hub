using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Services;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Monitoring;

public sealed class HealthCheckMonitor : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HealthCheckMonitor> _logger;
    private readonly GatewayMetrics _metrics;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(60);
    private readonly int _restartThreshold = 3;
    private readonly int _alertThreshold = 5;

    public HealthCheckMonitor(
        IServiceProvider serviceProvider,
        ILogger<HealthCheckMonitor> logger,
        GatewayMetrics metrics)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HealthCheckMonitor started");

        // Wait 10 seconds before first check to allow system to stabilize
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        using var timer = new PeriodicTimer(_checkInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunHealthChecksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running health checks");
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

        _logger.LogInformation("HealthCheckMonitor stopped");
    }

    private async Task RunHealthChecksAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var healthVerifier = scope.ServiceProvider.GetRequiredService<IHealthCheckVerifier>();
        var dockerService = scope.ServiceProvider.GetRequiredService<IDockerService>();
        var proxyManager = scope.ServiceProvider.GetRequiredService<ICaddyProxyManager>();
        var alertService = scope.ServiceProvider.GetRequiredService<IAlertService>();

        // Get all Running instances
        var runningInstances = await dbContext.ManagedInstances
            .Include(i => i.Infrastructure)
            .Include(i => i.Health)
            .Where(i => i.Status == InstanceStatus.Running && i.DeletedAt == null)
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Running health checks for {Count} instances", runningInstances.Count);

        foreach (var instance in runningInstances)
        {
            if (instance.Infrastructure == null)
            {
                _logger.LogWarning("Instance {InstanceId} has no infrastructure, skipping health check", instance.Id);
                continue;
            }

            // Ensure health record exists
            if (instance.Health == null)
            {
                instance.Health = new Entities.InstanceHealth
                {
                    ManagedInstanceId = instance.Id,
                    IsHealthy = true,
                    LastCheckAt = DateTimeOffset.UtcNow,
                    ConsecutiveFailures = 0
                };
                dbContext.InstanceHealths.Add(instance.Health);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await CheckInstanceHealthAsync(
                instance,
                healthVerifier,
                dockerService,
                proxyManager,
                alertService,
                dbContext,
                cancellationToken);
        }
    }

    private async Task CheckInstanceHealthAsync(
        Entities.ManagedInstance instance,
        IHealthCheckVerifier healthVerifier,
        IDockerService dockerService,
        ICaddyProxyManager proxyManager,
        IAlertService alertService,
        HubDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var health = instance.Health!;
        var infrastructure = instance.Infrastructure!;
        var allChecksPass = true;
        string? errorMessage = null;
        var responseTimeMs = 0;

        try
        {
            // 1. Verify container is running
            var containerRunning = await dockerService.VerifyContainerRunningAsync(
                infrastructure.DockerContainerId,
                cancellationToken);

            if (!containerRunning)
            {
                allChecksPass = false;
                errorMessage = "Container not running";
            }

            // 2. Verify proxy route
            if (allChecksPass)
            {
                var routeOk = await proxyManager.VerifyRouteAsync(
                    infrastructure.CaddyRouteId,
                    cancellationToken);

                if (!routeOk)
                {
                    allChecksPass = false;
                    errorMessage = "Proxy route not accessible";
                }
            }

            // 3. Verify health endpoint
            if (allChecksPass)
            {
                var (isHealthy, respTime, error) = await healthVerifier.VerifyInstanceHealthAsync(
                    instance.Domain,
                    cancellationToken);

                responseTimeMs = respTime;

                if (!isHealthy)
                {
                    allChecksPass = false;
                    errorMessage = error;
                }
            }

            // Update health record
            health.LastCheckAt = DateTimeOffset.UtcNow;
            health.ResponseTimeMs = responseTimeMs;

            if (allChecksPass)
            {
                // Reset consecutive failures on success
                if (health.ConsecutiveFailures > 0)
                {
                    _logger.LogInformation(
                        "Instance {InstanceId} ({Domain}) recovered after {Failures} failures",
                        instance.Id, instance.Domain, health.ConsecutiveFailures);
                }

                health.IsHealthy = true;
                health.ConsecutiveFailures = 0;
                health.ErrorMessage = null;

                // Record successful health check
                _metrics.RecordHealthCheck(success: true);
            }
            else
            {
                // Increment consecutive failures
                health.IsHealthy = false;
                health.ConsecutiveFailures++;
                health.ErrorMessage = errorMessage;

                _logger.LogWarning(
                    "Instance {InstanceId} ({Domain}) health check failed ({Failures} consecutive): {Error}",
                    instance.Id, instance.Domain, health.ConsecutiveFailures, errorMessage);

                // Record failed health check
                _metrics.RecordHealthCheck(success: false);

                // Handle remediation
                await HandleHealthFailureAsync(
                    instance,
                    dockerService,
                    alertService,
                    cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error checking health for instance {InstanceId} ({Domain})",
                instance.Id, instance.Domain);

            health.IsHealthy = false;
            health.ConsecutiveFailures++;
            health.ErrorMessage = $"Health check error: {ex.Message}";
            health.LastCheckAt = DateTimeOffset.UtcNow;

            // Record failed health check
            _metrics.RecordHealthCheck(success: false);

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task HandleHealthFailureAsync(
        Entities.ManagedInstance instance,
        IDockerService dockerService,
        IAlertService alertService,
        CancellationToken cancellationToken)
    {
        var health = instance.Health!;
        var infrastructure = instance.Infrastructure!;

        // 3 failures -> restart container
        if (health.ConsecutiveFailures == _restartThreshold)
        {
            _logger.LogWarning(
                "Instance {InstanceId} ({Domain}) reached {Threshold} consecutive failures, attempting restart",
                instance.Id, instance.Domain, _restartThreshold);

            try
            {
                // Stop and start container
                await dockerService.StopContainerAsync(infrastructure.DockerContainerId, cancellationToken);

                // Container restart is handled by Docker restart policy
                // Wait a moment for restart
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

                _logger.LogInformation(
                    "Initiated restart for instance {InstanceId} ({Domain})",
                    instance.Id, instance.Domain);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to restart instance {InstanceId} ({Domain})",
                    instance.Id, instance.Domain);
            }
        }

        // 5 failures -> send alert
        if (health.ConsecutiveFailures == _alertThreshold)
        {
            _logger.LogError(
                "Instance {InstanceId} ({Domain}) reached {Threshold} consecutive failures, sending alert",
                instance.Id, instance.Domain, _alertThreshold);

            await alertService.SendInstanceHealthAlertAsync(
                instance.Id,
                instance.Domain,
                health.ConsecutiveFailures,
                health.ErrorMessage ?? "Unknown error",
                cancellationToken);
        }
    }
}
