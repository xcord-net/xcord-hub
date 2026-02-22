using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Infrastructure.Services;

public sealed class DatabaseProvisioningQueue : IProvisioningQueue
{
    private readonly HubDbContext _dbContext;

    public DatabaseProvisioningQueue(HubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task EnqueueAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.ManagedInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance == null)
        {
            throw new InvalidOperationException($"Instance {instanceId} not found");
        }

        instance.Status = InstanceStatus.Provisioning;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<long?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.ManagedInstances
            .Where(i => i.Status == InstanceStatus.Provisioning && i.DeletedAt == null)
            .OrderBy(i => i.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return instance?.Id;
    }

    public async Task<List<long>> GetPendingInstancesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.ManagedInstances
            .Where(i => i.Status == InstanceStatus.Provisioning && i.DeletedAt == null)
            .OrderBy(i => i.CreatedAt)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
    }
}
