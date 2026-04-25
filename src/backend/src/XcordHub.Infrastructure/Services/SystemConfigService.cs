using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Infrastructure.Services;

public sealed class SystemConfigService(HubDbContext db) : ISystemConfigService
{
    public async Task<SystemConfig> GetAsync(CancellationToken ct = default)
    {
        var config = await db.SystemConfigs.FirstOrDefaultAsync(c => c.Id == SystemConfig.SingletonId, ct);
        if (config != null) return config;

        config = new SystemConfig
        {
            Id = SystemConfig.SingletonId,
            PaidServersDisabled = false,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.SystemConfigs.Add(config);
        await db.SaveChangesAsync(ct);
        return config;
    }

    public async Task<SystemConfig> SetPaidServersDisabledAsync(bool disabled, CancellationToken ct = default)
    {
        var config = await GetAsync(ct);
        config.PaidServersDisabled = disabled;
        config.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return config;
    }
}
