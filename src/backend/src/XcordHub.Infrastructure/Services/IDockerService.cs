namespace XcordHub.Infrastructure.Services;

public sealed record ContainerResourceLimits(long MemoryBytes, long CpuQuota);

public interface IDockerService
{
    Task<string> CreateNetworkAsync(string instanceDomain, CancellationToken cancellationToken = default);
    Task<bool> VerifyNetworkAsync(string networkId, CancellationToken cancellationToken = default);
    Task<string> CreateSecretAsync(string instanceDomain, string configJson, CancellationToken cancellationToken = default);
    Task<string> CreateRawSecretAsync(string secretName, string data, CancellationToken cancellationToken = default);
    Task RemoveSecretAsync(string secretId, CancellationToken cancellationToken = default);
    /// <param name="poolNetworkName">
    /// Pool-specific overlay network name (e.g. "xcord-pool-free") for production topologies.
    /// When null or empty, falls back to xcord-shared-net (dev/single-host mode).
    /// </param>
    Task<string> StartContainerAsync(string instanceDomain, string configSecretId, string? kekSecretId = null, ContainerResourceLimits? resourceLimits = null, string? poolNetworkName = null, CancellationToken cancellationToken = default);
    Task<bool> VerifyContainerRunningAsync(string containerId, CancellationToken cancellationToken = default);
    Task<bool> VerifyServiceExistsAsync(string serviceId, CancellationToken cancellationToken = default);
    /// <param name="poolNetworkName">
    /// Pool-specific overlay network name (e.g. "xcord-pool-free") for production topologies.
    /// When null or empty, falls back to xcord-shared-net (dev/single-host mode).
    /// </param>
    Task RunMigrationContainerAsync(string instanceDomain, string configJson, string? kekSecretId = null, string? poolNetworkName = null, CancellationToken cancellationToken = default);
    Task<bool> VerifyMigrationsCompleteAsync(string instanceDomain, CancellationToken cancellationToken = default);
    Task UpdateServiceImageAsync(string serviceId, string newImage, CancellationToken cancellationToken = default);
    Task StopContainerAsync(string containerId, CancellationToken cancellationToken = default);
    Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken = default);
    Task RemoveNetworkAsync(string networkId, CancellationToken cancellationToken = default);
}
