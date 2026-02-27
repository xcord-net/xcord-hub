namespace XcordHub.Infrastructure.Services;

public sealed record ContainerResourceLimits(long MemoryBytes, long CpuQuota);

public interface IDockerService
{
    Task<string> CreateNetworkAsync(string instanceDomain, CancellationToken cancellationToken = default);
    Task<bool> VerifyNetworkAsync(string networkId, CancellationToken cancellationToken = default);
    Task<string> CreateSecretAsync(string instanceDomain, string configJson, CancellationToken cancellationToken = default);
    Task RemoveSecretAsync(string secretId, CancellationToken cancellationToken = default);
    Task<string> StartContainerAsync(string instanceDomain, string secretId, ContainerResourceLimits? resourceLimits = null, CancellationToken cancellationToken = default);
    Task<bool> VerifyContainerRunningAsync(string containerId, CancellationToken cancellationToken = default);
    Task RunMigrationContainerAsync(string instanceDomain, string configJson, CancellationToken cancellationToken = default);
    Task<bool> VerifyMigrationsCompleteAsync(string instanceDomain, CancellationToken cancellationToken = default);
    Task StopContainerAsync(string containerId, CancellationToken cancellationToken = default);
    Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken = default);
    Task RemoveNetworkAsync(string networkId, CancellationToken cancellationToken = default);
}
