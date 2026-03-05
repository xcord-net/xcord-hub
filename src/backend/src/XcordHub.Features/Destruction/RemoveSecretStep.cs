using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Destruction;

public sealed class RemoveSecretStep(IDockerService dockerService, ILogger<RemoveSecretStep> logger) : IDestructionStep
{
    public string StepName => "RemoveSecret";

    public async Task ExecuteAsync(ManagedInstance instance, InstanceInfrastructure infrastructure, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(infrastructure.DockerSecretId))
        {
            logger.LogInformation("Removing Docker config secret {SecretId} for instance {Domain}", infrastructure.DockerSecretId, instance.Domain);
            await dockerService.RemoveSecretAsync(infrastructure.DockerSecretId, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(infrastructure.DockerKekSecretId))
        {
            logger.LogInformation("Removing Docker KEK secret {SecretId} for instance {Domain}", infrastructure.DockerKekSecretId, instance.Domain);
            await dockerService.RemoveSecretAsync(infrastructure.DockerKekSecretId, cancellationToken);
        }
    }
}
