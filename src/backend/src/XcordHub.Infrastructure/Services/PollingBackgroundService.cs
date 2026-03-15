using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Base class for background services that poll on a fixed interval.
/// Provides the standard while/try/catch/delay loop with start/stop logging.
/// </summary>
public abstract class PollingBackgroundService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger logger) : BackgroundService
{
    protected IServiceScopeFactory ServiceScopeFactory { get; } = serviceScopeFactory;
    protected ILogger Logger { get; } = logger;

    /// <summary>
    /// The interval between polling iterations.
    /// </summary>
    protected abstract TimeSpan Interval { get; }

    /// <summary>
    /// Override to perform the actual work each polling iteration.
    /// A new <see cref="IServiceScope"/> should be created inside this method
    /// via <see cref="ServiceScopeFactory"/> to resolve scoped services.
    /// </summary>
    protected abstract Task ProcessAsync(CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var name = GetType().Name;
        Logger.LogInformation("{ServiceName} started", name);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in {ServiceName}", name);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        Logger.LogInformation("{ServiceName} stopped", name);
    }
}
