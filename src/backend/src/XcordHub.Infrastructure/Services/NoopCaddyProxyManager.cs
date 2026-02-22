namespace XcordHub.Infrastructure.Services;

public sealed class NoopCaddyProxyManager : ICaddyProxyManager
{
    public Task<string> CreateRouteAsync(string instanceDomain, string containerName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"route_{instanceDomain}");
    }

    public Task<bool> VerifyRouteAsync(string routeId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public Task DeleteRouteAsync(string routeId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
