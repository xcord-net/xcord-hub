using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Destruction;

public sealed class StopContainerStep(IDockerService dockerService, ILogger<StopContainerStep> logger) : IDestructionStep
{
    public string StepName => "StopContainer";

    public async Task ExecuteAsync(ManagedInstance instance, InstanceInfrastructure infrastructure, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(infrastructure.DockerContainerId)) return;
        logger.LogInformation("Stopping container {ContainerId}", infrastructure.DockerContainerId);
        await dockerService.StopContainerAsync(infrastructure.DockerContainerId, cancellationToken);
    }
}
