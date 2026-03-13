using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub;

namespace XcordHub.Features.Provisioning;

public sealed class ConfigureBackupPolicyStep : IProvisioningStep
{
    private readonly HubDbContext _dbContext;

    public string StepName => "ConfigureBackupPolicy";

    public ConfigureBackupPolicyStep(HubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> ExecuteAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.BackupPolicies
            .FirstOrDefaultAsync(p => p.ManagedInstanceId == instanceId, cancellationToken);

        if (existing != null)
        {
            return true; // Already exists - idempotent
        }

        var now = DateTimeOffset.UtcNow;
        var policy = new BackupPolicy
        {
            ManagedInstanceId = instanceId,
            Enabled = true,
            Frequency = BackupFrequency.Daily,
            RetentionDays = 30,
            BackupDatabase = true,
            BackupFiles = true,
            BackupRedis = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.BackupPolicies.Add(policy);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Result<bool>> VerifyAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.BackupPolicies
            .AnyAsync(p => p.ManagedInstanceId == instanceId, cancellationToken);

        if (!exists)
        {
            return Error.Failure("BACKUP_POLICY_NOT_FOUND", "Backup policy was not created for the instance");
        }

        return true;
    }
}
