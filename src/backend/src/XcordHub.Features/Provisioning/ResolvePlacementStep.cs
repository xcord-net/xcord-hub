using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using XcordHub;

namespace XcordHub.Features.Provisioning;

public sealed class ResolvePlacementStep : IProvisioningStep
{
    private readonly HubDbContext _dbContext;
    private readonly TopologyResolver _resolver;

    public string StepName => "ResolvePlacement";

    public ResolvePlacementStep(HubDbContext dbContext, TopologyResolver resolver)
    {
        _dbContext = dbContext;
        _resolver = resolver;
    }

    public async Task<Result<bool>> ExecuteAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.ManagedInstances
            .Include(i => i.Billing)
            .Include(i => i.Infrastructure)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance?.Billing == null)
            return Error.NotFound("INSTANCE_NOT_FOUND", $"Instance {instanceId} or billing not found");

        if (instance.Infrastructure == null)
            return Error.NotFound("INFRASTRUCTURE_NOT_FOUND", $"Infrastructure for instance {instanceId} not found (run GenerateSecrets first)");

        if (!_resolver.IsConfigured)
        {
            instance.Infrastructure.PlacedInPool = "default";
            instance.Infrastructure.PlacementRegion = "";
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var topoTier = TopologyResolver.MapBillingTierToTopologyTier(
            instance.Billing.FeatureTier, instance.Billing.UserCountTier);

        if (topoTier == "enterprise")
        {
            var dedHost = _resolver.FindDedicatedHost("ded-1");
            if (dedHost != null)
            {
                instance.Infrastructure.PlacedInPool = $"dedicated:{dedHost.Id}";
                instance.Infrastructure.PlacementRegion = "";
                await _dbContext.SaveChangesAsync(cancellationToken);
                return true;
            }
        }

        var pool = _resolver.FindPoolForTier(topoTier);
        if (pool == null)
        {
            var fallbackOrder = new[] { "free", "basic", "pro" };
            var startIdx = Array.IndexOf(fallbackOrder, topoTier);
            for (var i = Math.Max(0, startIdx + 1); i < fallbackOrder.Length; i++)
            {
                pool = _resolver.FindPoolForTier(fallbackOrder[i]);
                if (pool != null) break;
            }
        }

        if (pool == null)
            return Error.Failure("NO_POOL_AVAILABLE", $"No compute pool available for tier '{topoTier}'");

        var currentCount = await _dbContext.InstanceInfrastructures
            .CountAsync(i => i.PlacedInPool == pool.Name
                && i.ManagedInstance.Status != InstanceStatus.Destroyed
                && i.ManagedInstance.Status != InstanceStatus.Failed,
                cancellationToken);

        if (pool.Capacity.TenantSlots > 0 && currentCount >= pool.Capacity.TenantSlots)
            return Error.Failure("POOL_AT_CAPACITY", $"Pool '{pool.Name}' is at capacity ({currentCount}/{pool.Capacity.TenantSlots})");

        instance.Infrastructure.PlacedInPool = pool.Name;
        instance.Infrastructure.PlacementRegion = "";
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Result<bool>> VerifyAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var infra = await _dbContext.InstanceInfrastructures
            .FirstOrDefaultAsync(i => i.ManagedInstanceId == instanceId, cancellationToken);

        if (infra == null)
            return Error.NotFound("INFRASTRUCTURE_NOT_FOUND", $"Infrastructure for {instanceId} not found");

        return !string.IsNullOrWhiteSpace(infra.PlacedInPool)
            ? true
            : Error.Failure("PLACEMENT_NOT_SET", "PlacedInPool has not been set");
    }
}
