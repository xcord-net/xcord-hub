using XcordHub.Entities;

namespace XcordHub.Infrastructure.Services;

public interface ISystemConfigService
{
    Task<SystemConfig> GetAsync(CancellationToken ct = default);
    Task<SystemConfig> SetPaidServersDisabledAsync(bool disabled, CancellationToken ct = default);
}
