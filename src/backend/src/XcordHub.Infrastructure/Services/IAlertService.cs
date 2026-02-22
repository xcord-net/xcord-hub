namespace XcordHub.Infrastructure.Services;

public interface IAlertService
{
    Task SendInstanceHealthAlertAsync(
        long instanceId,
        string domain,
        int consecutiveFailures,
        string errorMessage,
        CancellationToken cancellationToken = default);
}
