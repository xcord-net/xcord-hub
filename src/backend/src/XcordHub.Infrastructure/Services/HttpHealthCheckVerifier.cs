using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace XcordHub.Infrastructure.Services;

public sealed class HttpHealthCheckVerifier : IHealthCheckVerifier
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpHealthCheckVerifier> _logger;

    public HttpHealthCheckVerifier(HttpClient httpClient, ILogger<HttpHealthCheckVerifier> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<(bool IsHealthy, int ResponseTimeMs, string? ErrorMessage)> VerifyInstanceHealthAsync(
        string domain,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Use the container's internal Docker hostname (xcord-{subdomain}-api:80)
            // instead of the public domain, so the check works from inside the Docker network.
            var subdomain = ValidationHelpers.ExtractSubdomain(domain);
            var containerHost = $"xcord-{subdomain}-api";
            var healthUrl = $"http://{containerHost}:80/api/v1/health";
            var response = await _httpClient.GetAsync(healthUrl, cancellationToken);

            stopwatch.Stop();
            var responseTimeMs = (int)stopwatch.ElapsedMilliseconds;

            if (response.IsSuccessStatusCode)
            {
                return (true, responseTimeMs, null);
            }

            var errorMessage = $"Health endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}";
            _logger.LogWarning("Health check failed for {Domain}: {Error}", domain, errorMessage);
            return (false, responseTimeMs, errorMessage);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var responseTimeMs = (int)stopwatch.ElapsedMilliseconds;
            var errorMessage = $"Health check failed: {ex.Message}";

            _logger.LogError(ex, "Health check error for {Domain}", domain);
            return (false, responseTimeMs, errorMessage);
        }
    }
}
