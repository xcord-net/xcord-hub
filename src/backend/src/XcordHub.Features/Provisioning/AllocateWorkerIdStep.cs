using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub;

namespace XcordHub.Features.Provisioning;

public sealed class AllocateWorkerIdStep : IProvisioningStep
{
    private readonly HubDbContext _dbContext;

    public string StepName => "AllocateWorkerId";

    // WorkerIds 1-10 reserved for infrastructure (hub, etc.)
    private const int MinWorkerId = 11;
    private const int MaxWorkerId = 1023; // Snowflake limit

    public AllocateWorkerIdStep(HubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> ExecuteAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.ManagedInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance == null)
        {
            return Error.NotFound("INSTANCE_NOT_FOUND", $"Instance {instanceId} not found");
        }

        // Skip if already allocated
        if (instance.SnowflakeWorkerId > 0)
        {
            return true;
        }

        // Find the next available worker ID
        var allocatedWorkerIds = (await _dbContext.Set<WorkerIdRegistry>()
            .Where(w => !w.IsTombstoned)
            .Select(w => w.WorkerId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        int? availableWorkerId = null;
        for (int i = MinWorkerId; i <= MaxWorkerId; i++)
        {
            if (!allocatedWorkerIds.Contains(i))
            {
                availableWorkerId = i;
                break;
            }
        }

        if (availableWorkerId == null)
        {
            return Error.Failure("NO_WORKER_IDS_AVAILABLE", "No worker IDs available for allocation");
        }

        // Allocate the worker ID
        var registry = new WorkerIdRegistry
        {
            WorkerId = availableWorkerId.Value,
            ManagedInstanceId = instanceId,
            IsTombstoned = false,
            AllocatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Set<WorkerIdRegistry>().Add(registry);

        instance.SnowflakeWorkerId = availableWorkerId.Value;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<Result<bool>> VerifyAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.ManagedInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance == null)
        {
            return Error.NotFound("INSTANCE_NOT_FOUND", $"Instance {instanceId} not found");
        }

        if (instance.SnowflakeWorkerId < MinWorkerId || instance.SnowflakeWorkerId > MaxWorkerId)
        {
            return Error.Failure("WORKER_ID_INVALID", "Worker ID is invalid or not allocated");
        }

        var registry = await _dbContext.Set<WorkerIdRegistry>()
            .FirstOrDefaultAsync(w => w.WorkerId == instance.SnowflakeWorkerId && w.ManagedInstanceId == instanceId, cancellationToken);

        if (registry == null)
        {
            return Error.Failure("WORKER_ID_NOT_REGISTERED", "Worker ID is not registered in the registry");
        }

        return true;
    }
}
