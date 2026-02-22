using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace XcordHub.Infrastructure.Services;

public sealed class WebhookAlertService : IAlertService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookAlertService> _logger;
    private readonly string? _webhookUrl;

    public WebhookAlertService(
        HttpClient httpClient,
        ILogger<WebhookAlertService> logger,
        string? webhookUrl = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _webhookUrl = webhookUrl;
    }

    public async Task SendInstanceHealthAlertAsync(
        long instanceId,
        string domain,
        int consecutiveFailures,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl))
        {
            _logger.LogWarning("No webhook URL configured, skipping health alert for instance {InstanceId}", instanceId);
            return;
        }

        try
        {
            var payload = new
            {
                Type = "instance_health_critical",
                InstanceId = instanceId,
                Domain = domain,
                ConsecutiveFailures = consecutiveFailures,
                ErrorMessage = errorMessage,
                Timestamp = DateTimeOffset.UtcNow
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_webhookUrl, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation(
                "Health alert sent for instance {InstanceId} ({Domain}) - {Failures} failures",
                instanceId, domain, consecutiveFailures);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send health alert for instance {InstanceId}: {Error}",
                instanceId, ex.Message);
        }
    }
}
