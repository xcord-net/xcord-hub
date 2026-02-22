namespace XcordHub.Infrastructure.Services;

public interface IDockerService
{
    Task<string> CreateNetworkAsync(string instanceDomain, CancellationToken cancellationToken = default);
    Task<bool> VerifyNetworkAsync(string networkId, CancellationToken cancellationToken = default);
    Task<string> StartContainerAsync(string instanceDomain, string configJson, CancellationToken cancellationToken = default);
    Task<bool> VerifyContainerRunningAsync(string containerId, CancellationToken cancellationToken = default);
    Task RunMigrationContainerAsync(string instanceDomain, CancellationToken cancellationToken = default);
    Task<bool> VerifyMigrationsCompleteAsync(string instanceDomain, CancellationToken cancellationToken = default);
    Task StopContainerAsync(string containerId, CancellationToken cancellationToken = default);
    Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken = default);
    Task RemoveNetworkAsync(string networkId, CancellationToken cancellationToken = default);
}
