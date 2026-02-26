using Microsoft.Extensions.Logging;
using XcordHub.Entities;

namespace XcordHub.Features.Destruction;

public sealed class DestructionPipeline(
    IEnumerable<IDestructionStep> steps,
    ILogger<DestructionPipeline> logger)
{
    private readonly List<IDestructionStep> _steps = steps.ToList();

    public async Task RunAsync(ManagedInstance instance, InstanceInfrastructure infrastructure, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting destruction pipeline for instance {InstanceId} ({Domain})", instance.Id, instance.Domain);

        foreach (var step in _steps)
        {
            try
            {
                await step.ExecuteAsync(instance, infrastructure, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Destruction step {StepName} failed for instance {InstanceId}, continuing cleanup", step.StepName, instance.Id);
            }
        }

        logger.LogInformation("Destruction pipeline completed for instance {InstanceId} ({Domain})", instance.Id, instance.Domain);
    }
}
