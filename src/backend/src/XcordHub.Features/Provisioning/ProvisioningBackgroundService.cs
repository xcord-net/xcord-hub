using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Provisioning;

public sealed class ProvisioningBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProvisioningBackgroundService> _logger;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    public ProvisioningBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<ProvisioningBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Provisioning background service started");

        // On startup, resume any pending instances
        await ResumePendingInstances(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextInstance(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing provisioning queue");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }

        _logger.LogInformation("Provisioning background service stopped");
    }

    private async Task ResumePendingInstances(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<IProvisioningQueue>();

            var pendingInstances = await queue.GetPendingInstancesAsync(cancellationToken);

            if (pendingInstances.Count > 0)
            {
                _logger.LogInformation("Found {Count} pending instances to resume", pendingInstances.Count);

                foreach (var instanceId in pendingInstances)
                {
                    _logger.LogInformation("Resuming provisioning for instance {InstanceId}", instanceId);
                    await ProcessInstance(instanceId, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resuming pending instances");
        }
    }

    private async Task ProcessNextInstance(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IProvisioningQueue>();

        var instanceId = await queue.DequeueAsync(cancellationToken);

        if (instanceId.HasValue)
        {
            _logger.LogInformation("Dequeued instance {InstanceId} for provisioning", instanceId.Value);
            await ProcessInstance(instanceId.Value, cancellationToken);
        }
    }

    private async Task ProcessInstance(long instanceId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var pipeline = scope.ServiceProvider.GetRequiredService<ProvisioningPipeline>();

            var result = await pipeline.RunAsync(instanceId, cancellationToken);

            if (result.IsSuccess)
            {
                _logger.LogInformation("Instance {InstanceId} provisioned successfully", instanceId);
            }
            else
            {
                _logger.LogError("Instance {InstanceId} provisioning failed: {Error}",
                    instanceId, result.Error?.Message ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running provisioning pipeline for instance {InstanceId}", instanceId);
        }
    }
}
