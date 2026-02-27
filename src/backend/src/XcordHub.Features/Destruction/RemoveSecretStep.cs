using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Destruction;

public sealed class RemoveSecretStep(IDockerService dockerService, ILogger<RemoveSecretStep> logger) : IDestructionStep
{
    public string StepName => "RemoveSecret";

    public async Task ExecuteAsync(ManagedInstance instance, InstanceInfrastructure infrastructure, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(infrastructure.DockerSecretId)) return;
        logger.LogInformation("Removing Docker secret {SecretId} for instance {Domain}", infrastructure.DockerSecretId, instance.Domain);
        await dockerService.RemoveSecretAsync(infrastructure.DockerSecretId, cancellationToken);
    }
}
