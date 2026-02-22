using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub;

namespace XcordHub.Features.Provisioning;

public sealed class ValidateSubdomainStep : IProvisioningStep
{
    private readonly HubDbContext _dbContext;

    public string StepName => "ValidateSubdomain";

    public ValidateSubdomainStep(HubDbContext dbContext)
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

        // Check if domain is already taken (excluding soft-deleted instances)
        var domainExists = await _dbContext.ManagedInstances
            .AnyAsync(i => i.Domain == instance.Domain && i.Id != instanceId && i.DeletedAt == null, cancellationToken);

        if (domainExists)
        {
            return Error.Conflict("DOMAIN_TAKEN", $"Domain {instance.Domain} is already taken");
        }

        return true;
    }

    public Task<Result<bool>> VerifyAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        // Validation is atomic, no separate verification needed
        return Task.FromResult<Result<bool>>(true);
    }
}
