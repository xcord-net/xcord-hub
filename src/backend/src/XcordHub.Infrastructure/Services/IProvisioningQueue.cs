namespace XcordHub.Infrastructure.Services;

public interface IProvisioningQueue
{
    Task EnqueueAsync(long instanceId, CancellationToken cancellationToken = default);
    Task<long?> DequeueAsync(CancellationToken cancellationToken = default);
    Task<List<long>> GetPendingInstancesAsync(CancellationToken cancellationToken = default);
}
