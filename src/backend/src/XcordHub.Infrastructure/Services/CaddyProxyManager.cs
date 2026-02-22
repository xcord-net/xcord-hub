using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace XcordHub.Infrastructure.Services;

public sealed class CaddyProxyManager : ICaddyProxyManager
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CaddyProxyManager> _logger;

    public CaddyProxyManager(IHttpClientFactory httpClientFactory, ILogger<CaddyProxyManager> logger)
    {
        _httpClient = httpClientFactory.CreateClient("CaddyAdmin");
        _logger = logger;
    }

    public async Task<string> CreateRouteAsync(string instanceDomain, string containerName, CancellationToken cancellationToken = default)
    {
        var subdomain = instanceDomain.Split('.')[0];
        var routeId = $"xcord-{subdomain}-route";

        // Caddy Admin API route configuration with @id for later retrieval/deletion.
        // Appended to the srv0 routes array — evaluated after the hub-domain route,
        // which only matches the exact hub domain, so subdomain requests fall through.
        var route = new Dictionary<string, object>
        {
            ["@id"] = routeId,
            ["match"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["host"] = new[] { instanceDomain }
                }
            },
            ["handle"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["handler"] = "reverse_proxy",
                    ["upstreams"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["dial"] = $"{containerName}:80"
                        }
                    }
                }
            },
            ["terminal"] = true
        };

        _logger.LogInformation("Creating Caddy route {RouteId} for instance {Domain} -> {ContainerName}",
            routeId, instanceDomain, containerName);

        // POST to the routes array to append a new route
        var response = await _httpClient.PostAsJsonAsync(
            "/config/apps/http/servers/srv0/routes",
            route,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to create Caddy route: {error}");
        }

        _logger.LogInformation("Created Caddy route {RouteId} for instance {Domain}", routeId, instanceDomain);
        return routeId;
    }

    public async Task<bool> VerifyRouteAsync(string routeId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Use Caddy's /id/ endpoint to look up the route by its @id field
            var response = await _httpClient.GetAsync(
                $"/id/{routeId}",
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify Caddy route {RouteId}", routeId);
            return false;
        }
    }

    public async Task DeleteRouteAsync(string routeId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting Caddy route {RouteId}", routeId);

        // Use Caddy's /id/ endpoint to delete the route by its @id field
        var response = await _httpClient.DeleteAsync(
            $"/id/{routeId}",
            cancellationToken);

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to delete Caddy route: {error}");
        }

        _logger.LogInformation("Deleted Caddy route {RouteId}", routeId);
    }
}
