using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Destruction;

public sealed class RemoveProxyRouteStep(ICaddyProxyManager proxyManager, ILogger<RemoveProxyRouteStep> logger) : IDestructionStep
{
    public string StepName => "RemoveProxyRoute";

    public async Task ExecuteAsync(ManagedInstance instance, InstanceInfrastructure infrastructure, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(infrastructure.CaddyRouteId)) return;
        logger.LogInformation("Removing proxy route {RouteId}", infrastructure.CaddyRouteId);
        await proxyManager.DeleteRouteAsync(infrastructure.CaddyRouteId, cancellationToken);
    }
}
