using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Sends lifecycle notifications to xcord-fed instance containers via their internal HTTP API.
/// Calls the instance using its Docker container hostname (xcord-{subdomain}-api:80) so the
/// request stays within the Docker network and does not require public DNS resolution.
/// All failures are swallowed — if the instance is already unreachable the hub proceeds
/// with the lifecycle operation (suspend/destroy) regardless.
/// </summary>
public sealed class HttpInstanceNotifier : IInstanceNotifier
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpInstanceNotifier> _logger;

    public HttpInstanceNotifier(HttpClient httpClient, ILogger<HttpInstanceNotifier> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task NotifyShuttingDownAsync(
        string instanceDomain,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var subdomain = ValidationHelpers.ExtractSubdomain(instanceDomain);
        var containerHost = $"xcord-{subdomain}-api";
        var url = $"http://{containerHost}:80/api/v1/internal/shutdown";

        _logger.LogInformation(
            "Sending System_ShuttingDown notification to instance {Domain} ({ContainerHost}), reason: {Reason}",
            instanceDomain, containerHost, reason);

        try
        {
            var payload = new { Reason = reason };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(4));

            var response = await _httpClient.PostAsJsonAsync(url, payload, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "System_ShuttingDown notification acknowledged by instance {Domain}",
                    instanceDomain);
            }
            else
            {
                _logger.LogWarning(
                    "Instance {Domain} returned {StatusCode} for shutdown notification, proceeding with suspension",
                    instanceDomain, (int)response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Timeout sending shutdown notification to instance {Domain}, proceeding with suspension",
                instanceDomain);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send shutdown notification to instance {Domain}, proceeding with suspension",
                instanceDomain);
        }
    }
}
