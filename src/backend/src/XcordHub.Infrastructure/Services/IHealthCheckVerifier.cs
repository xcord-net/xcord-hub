namespace XcordHub.Infrastructure.Services;

public interface IHealthCheckVerifier
{
    Task<(bool IsHealthy, int ResponseTimeMs, string? ErrorMessage, string? Version)> VerifyInstanceHealthAsync(
        string domain,
        CancellationToken cancellationToken = default);
}
