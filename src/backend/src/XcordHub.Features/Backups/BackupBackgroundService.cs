using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Backups;

public sealed class BackupBackgroundService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<BackupBackgroundService> logger) : PollingBackgroundService(serviceScopeFactory, logger)
{
    private bool? _coldStorageConfigured;

    protected override TimeSpan Interval => TimeSpan.FromSeconds(60);

    protected override async Task ProcessAsync(CancellationToken ct)
    {
        // Check once whether cold storage is configured — skip entirely if not
        if (_coldStorageConfigured == null)
        {
            using var checkScope = ServiceScopeFactory.CreateScope();
            var coldStorage = checkScope.ServiceProvider.GetRequiredService<IColdStorageService>();
            _coldStorageConfigured = coldStorage.IsConfigured;

            if (!_coldStorageConfigured.Value)
            {
                Logger.LogInformation("BackupBackgroundService disabled — cold storage not configured");
            }
        }

        if (!_coldStorageConfigured.Value)
            return;

        await CheckAndRunBackupsAsync(ct);
    }

    private async Task CheckAndRunBackupsAsync(CancellationToken ct)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var executor = scope.ServiceProvider.GetRequiredService<BackupExecutor>();

        var policies = await dbContext.BackupPolicies
            .Include(p => p.ManagedInstance)
            .Where(p => p.Enabled && p.ManagedInstance.Status == InstanceStatus.Running
                && p.ManagedInstance.DeletedAt == null)
            .ToListAsync(ct);

        foreach (var policy in policies)
        {
            var lastBackup = await dbContext.BackupRecords
                .Where(r => r.ManagedInstanceId == policy.ManagedInstanceId
                    && r.Status != BackupStatus.Failed)
                .OrderByDescending(r => r.StartedAt)
                .FirstOrDefaultAsync(ct);

            if (!IsBackupDue(policy, lastBackup))
                continue;

            Logger.LogInformation("Backup due for {Domain} (frequency: {Frequency})",
                policy.ManagedInstance.Domain, policy.Frequency);

            try
            {
                await executor.ExecuteBackupAsync(policy.ManagedInstance, BackupKind.Full, ct);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to execute backup for {Domain}",
                    policy.ManagedInstance.Domain);
            }
        }
    }

    private static bool IsBackupDue(BackupPolicy policy, BackupRecord? lastBackup)
    {
        if (lastBackup == null) return true;

        var interval = policy.Frequency switch
        {
            BackupFrequency.Hourly => TimeSpan.FromHours(1),
            BackupFrequency.Daily => TimeSpan.FromDays(1),
            BackupFrequency.Weekly => TimeSpan.FromDays(7),
            _ => TimeSpan.FromDays(1)
        };

        return DateTimeOffset.UtcNow - lastBackup.StartedAt >= interval;
    }
}
