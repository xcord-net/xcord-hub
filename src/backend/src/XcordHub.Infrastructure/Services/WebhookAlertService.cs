using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Posts the hub's own alarms to the spark's notify acceptor
/// (POST http://100.64.0.3:8700/notify/xcord), which fans out to the desktop and,
/// for warning and critical, to email as well.
///
/// The payload shape is that endpoint's contract, not one of our own: it requires
/// a `title` and a `severity` of exactly info|warning|critical, rejects anything
/// else with 422, and answers 401 without the ingest bearer token. The payload
/// this used to send - Type/InstanceId/Domain/ConsecutiveFailures/Timestamp,
/// PascalCase, no title and no severity - would have failed all three checks had
/// a URL ever been configured to send it to.
/// </summary>
public sealed class WebhookAlertService : IAlertService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookAlertService> _logger;
    private readonly string? _webhookUrl;
    private readonly string? _webhookToken;

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public WebhookAlertService(
        HttpClient httpClient,
        ILogger<WebhookAlertService> logger,
        string? webhookUrl = null,
        string? webhookToken = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _webhookUrl = webhookUrl;
        _webhookToken = webhookToken;
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
            var payload = new NotifyPayload
            {
                // critical, not warning: this fires at five consecutive failed
                // health checks, by which point the automatic restart at three
                // has already been tried and has not fixed it.
                Severity = "critical",
                Title = $"xcord instance unhealthy: {domain}",
                Body = $"{consecutiveFailures} consecutive health-check failures. {errorMessage}",
                // Collapses repeats for the same instance inside 30 minutes, so a
                // flapping instance is one notification rather than forty.
                DedupeKey = $"instance-health:{instanceId}",
                Url = $"https://{domain}"
            };

            var json = JsonSerializer.Serialize(payload, PayloadOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, _webhookUrl) { Content = content };

            if (!string.IsNullOrWhiteSpace(_webhookToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _webhookToken);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// The notify acceptor's ingest body: snake_case on the wire, `title` and a
    /// recognised `severity` mandatory.
    /// </summary>
    internal sealed record NotifyPayload
    {
        [JsonPropertyName("severity")]
        public required string Severity { get; init; }

        [JsonPropertyName("title")]
        public required string Title { get; init; }

        [JsonPropertyName("body")]
        public string Body { get; init; } = string.Empty;

        [JsonPropertyName("dedupe_key")]
        public string? DedupeKey { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }
    }
}
