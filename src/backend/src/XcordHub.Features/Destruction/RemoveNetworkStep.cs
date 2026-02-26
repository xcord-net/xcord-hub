using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Destruction;

public sealed class RemoveNetworkStep(IDockerService dockerService, ILogger<RemoveNetworkStep> logger) : IDestructionStep
{
    public string StepName => "RemoveNetwork";

    public async Task ExecuteAsync(ManagedInstance instance, InstanceInfrastructure infrastructure, CancellationToken cancellationToken)
    {
        logger.LogInformation("Removing network for {Domain}", instance.Domain);
        await dockerService.RemoveNetworkAsync(instance.Domain, cancellationToken);
    }
}
