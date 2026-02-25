using Microsoft.EntityFrameworkCore;
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
            .Include(i => i.Config)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance?.Billing == null)
        {
            return Error.NotFound("INSTANCE_NOT_FOUND", $"Instance {instanceId} or billing not found");
        }

        // Resolve resource limits from the billing tier
        var resourceLimits = TierDefaults.GetResourceLimits(instance.Billing.UserCountTier);
        var featureFlags = TierDefaults.GetFeatureFlags(instance.Billing.FeatureTier, instance.Billing.HdUpgrade);

        // Store resolved limits in InstanceConfig so StartApiContainerStep can apply them
        if (instance.Config == null)
        {
            instance.Config = new Entities.InstanceConfig
            {
                ManagedInstanceId = instanceId,
                ConfigJson = "{}",
                ResourceLimitsJson = System.Text.Json.JsonSerializer.Serialize(resourceLimits),
                FeatureFlagsJson = System.Text.Json.JsonSerializer.Serialize(featureFlags),
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _dbContext.InstanceConfigs.Add(instance.Config);
        }
        else
        {
            instance.Config.ResourceLimitsJson = System.Text.Json.JsonSerializer.Serialize(resourceLimits);
            instance.Config.FeatureFlagsJson = System.Text.Json.JsonSerializer.Serialize(featureFlags);
            instance.Config.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Result<bool>> VerifyAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.InstanceConfigs
            .FirstOrDefaultAsync(c => c.ManagedInstanceId == instanceId, cancellationToken);

        if (config == null || string.IsNullOrWhiteSpace(config.ResourceLimitsJson))
        {
            return Error.Failure("LIMITS_NOT_SET", "Resource limits have not been stored");
        }

        return true;
    }
}
