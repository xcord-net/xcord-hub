namespace XcordHub.Infrastructure.Services;

public interface ICaddyProxyManager
{
    Task<string> CreateRouteAsync(string instanceDomain, string containerName, CancellationToken cancellationToken = default);
    Task<bool> VerifyRouteAsync(string routeId, CancellationToken cancellationToken = default);
    Task DeleteRouteAsync(string routeId, CancellationToken cancellationToken = default);
}
