using Microsoft.Extensions.Logging;

namespace XcordHub.Infrastructure.Services;

public sealed class NoopDockerService : IDockerService
{
    private readonly ILogger<NoopDockerService> _logger;

    public NoopDockerService(ILogger<NoopDockerService> logger)
    {
        _logger = logger;
    }

    public Task<string> CreateNetworkAsync(string instanceDomain, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NOOP: Would create network for {Domain}", instanceDomain);
        return Task.FromResult($"network_{instanceDomain}");
    }

    public Task<bool> VerifyNetworkAsync(string networkId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NOOP: Would verify network {NetworkId}", networkId);
        return Task.FromResult(true);
    }

    public Task<string> StartContainerAsync(string instanceDomain, string configJson, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NOOP: Would start container for {Domain}", instanceDomain);
        return Task.FromResult($"container_{instanceDomain}");
    }

    public Task<bool> VerifyContainerRunningAsync(string containerId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NOOP: Would verify container {ContainerId} is running", containerId);
        return Task.FromResult(true);
    }

    public Task RunMigrationContainerAsync(string instanceDomain, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NOOP: Would run migrations for {Domain}", instanceDomain);
        return Task.CompletedTask;
    }

    public Task<bool> VerifyMigrationsCompleteAsync(string instanceDomain, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NOOP: Would verify migrations complete for {Domain}", instanceDomain);
        return Task.FromResult(true);
    }

    public Task StopContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NOOP: Would stop container {ContainerId}", containerId);
        return Task.CompletedTask;
    }

    public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NOOP: Would remove container {ContainerId}", containerId);
        return Task.CompletedTask;
    }

    public Task RemoveNetworkAsync(string networkId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NOOP: Would remove network {NetworkId}", networkId);
        return Task.CompletedTask;
    }
}
