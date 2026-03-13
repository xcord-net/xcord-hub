using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Backups;

public sealed class BackupBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackupBackgroundService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(60);

    public BackupBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<BackupBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BackupBackgroundService started");

        // Wait before first check to allow system to stabilize
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        using var timer = new PeriodicTimer(_checkInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndRunBackupsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking backup schedules");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("BackupBackgroundService stopped");
    }

    private async Task CheckAndRunBackupsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
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

            _logger.LogInformation("Backup due for {Domain} (frequency: {Frequency})",
                policy.ManagedInstance.Domain, policy.Frequency);

            try
            {
                await executor.ExecuteBackupAsync(policy.ManagedInstance, BackupKind.Full, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute backup for {Domain}",
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
