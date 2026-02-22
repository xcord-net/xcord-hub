using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Data;
using XcordHub;

namespace XcordHub.Features.Provisioning;

public sealed class EnforceTierLimitsStep : IProvisioningStep
{
    private readonly HubDbContext _dbContext;

    public string StepName => "EnforceTierLimits";

    public EnforceTierLimitsStep(HubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> ExecuteAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.ManagedInstances
            .Include(i => i.Billing)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance?.Billing == null)
        {
            return Error.NotFound("INSTANCE_NOT_FOUND", $"Instance {instanceId} or billing not found");
        }

        // Get max instances for this tier
        var maxInstances = TierDefaults.GetMaxInstancesForTier(instance.Billing.Tier);

        // If unlimited (-1), allow
        if (maxInstances == -1)
        {
            return true;
        }

        // Count active instances for this owner
        var ownerInstanceCount = await _dbContext.ManagedInstances
            .Where(i => i.OwnerId == instance.OwnerId && i.DeletedAt == null)
            .CountAsync(cancellationToken);

        if (ownerInstanceCount > maxInstances)
        {
            return Error.Forbidden("TIER_LIMIT_EXCEEDED", $"{instance.Billing.Tier} tier limit of {maxInstances} instances exceeded");
        }

        return true;
    }

    public Task<Result<bool>> VerifyAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        // Tier enforcement is atomic, no separate verification needed
        return Task.FromResult<Result<bool>>(true);
    }
}
