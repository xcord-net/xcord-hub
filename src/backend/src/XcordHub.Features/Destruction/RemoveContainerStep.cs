using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Destruction;

public sealed class RemoveContainerStep(IDockerService dockerService, ILogger<RemoveContainerStep> logger) : IDestructionStep
{
    public string StepName => "RemoveContainer";

    public async Task ExecuteAsync(ManagedInstance instance, InstanceInfrastructure infrastructure, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(infrastructure.DockerContainerId)) return;
        logger.LogInformation("Removing container {ContainerId}", infrastructure.DockerContainerId);
        await dockerService.RemoveContainerAsync(infrastructure.DockerContainerId, cancellationToken);
    }
}
