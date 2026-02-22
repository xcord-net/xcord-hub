using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using XcordHub;

namespace XcordHub.Features.Provisioning;

public sealed class CreateNetworkStep : IProvisioningStep
{
    private readonly HubDbContext _dbContext;
    private readonly IDockerService _dockerService;

    public string StepName => "CreateNetwork";

    public CreateNetworkStep(HubDbContext dbContext, IDockerService dockerService)
    {
        _dbContext = dbContext;
        _dockerService = dockerService;
    }

    public async Task<Result<bool>> ExecuteAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.ManagedInstances
            .Include(i => i.Infrastructure)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance?.Infrastructure == null)
        {
            return Error.NotFound("INSTANCE_NOT_FOUND", $"Instance {instanceId} or infrastructure not found");
        }

        try
        {
            var networkId = await _dockerService.CreateNetworkAsync(instance.Domain, cancellationToken);
            instance.Infrastructure.DockerNetworkId = networkId;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            return Error.Failure("NETWORK_CREATION_FAILED", $"Failed to create network: {ex.Message}");
        }
    }

    public async Task<Result<bool>> VerifyAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var infrastructure = await _dbContext.InstanceInfrastructures
            .FirstOrDefaultAsync(i => i.ManagedInstanceId == instanceId, cancellationToken);

        if (infrastructure == null)
        {
            return Error.NotFound("INFRASTRUCTURE_NOT_FOUND", $"Infrastructure for instance {instanceId} not found");
        }

        if (string.IsNullOrWhiteSpace(infrastructure.DockerNetworkId))
        {
            return Error.Failure("NETWORK_ID_MISSING", "Network ID is missing");
        }

        try
        {
            var exists = await _dockerService.VerifyNetworkAsync(infrastructure.DockerNetworkId, cancellationToken);
            return exists ? true : Error.Failure("NETWORK_VERIFY_FAILED", "Network verification failed");
        }
        catch (Exception ex)
        {
            return Error.Failure("NETWORK_VERIFY_ERROR", $"Network verification error: {ex.Message}");
        }
    }
}
