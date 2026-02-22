namespace XcordHub.Infrastructure.Services;

public interface IInstanceNotifier
{
    /// <summary>
    /// Sends a System_ShuttingDown notification to the instance before it is stopped.
    /// If the instance is unreachable, the failure is logged and suppressed so suspension can proceed.
    /// </summary>
    Task NotifyShuttingDownAsync(
        string instanceDomain,
        string reason,
        CancellationToken cancellationToken = default);
}
