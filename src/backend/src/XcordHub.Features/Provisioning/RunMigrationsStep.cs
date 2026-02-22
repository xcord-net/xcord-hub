using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using XcordHub;

namespace XcordHub.Features.Provisioning;

/// <summary>
/// Migration step for provisioned xcord-fed instances.
/// xcord-fed applies EF Core migrations automatically at startup via MigrateAsync(),
/// so this step is a lightweight readiness check rather than a separate migration run.
/// The instance container must be started first (StartApiContainer step), after which
/// the app handles its own schema migration before the health endpoint becomes ready.
/// </summary>
public sealed class RunMigrationsStep : IProvisioningStep
{
    private readonly HubDbContext _dbContext;

    public string StepName => "RunMigrations";

    public RunMigrationsStep(HubDbContext dbContext)
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

        // xcord-fed applies its own migrations at startup via MigrateAsync().
        // No separate migration container is needed; this step is a no-op.
        return true;
    }

    public Task<Result<bool>> VerifyAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        // Verification is performed by the StartApiContainer + ConfigureDnsAndProxy steps
        // which wait until the instance container is running and the health endpoint responds.
        return Task.FromResult<Result<bool>>(true);
    }
}
