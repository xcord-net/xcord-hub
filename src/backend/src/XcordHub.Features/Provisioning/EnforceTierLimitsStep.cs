using Microsoft.EntityFrameworkCore;
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

        // No per-user instance limits in the new billing model.
        // Billing is per-instance with FeatureTier + UserCountTier.
        return true;
    }

    public Task<Result<bool>> VerifyAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Result<bool>>(true);
    }
}
