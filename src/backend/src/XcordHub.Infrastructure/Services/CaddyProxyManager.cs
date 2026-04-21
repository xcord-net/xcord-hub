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
        var subdomain = ValidationHelpers.ExtractSubdomain(instanceDomain);
        var routeId = $"xcord-{subdomain}-route";

        // Caddy Admin API route configuration with @id for later retrieval/deletion.
        // Appended to the srv0 routes array - evaluated after the hub-domain route,
        // which only matches the exact hub domain, so subdomain requests fall through.
        //
        // The instance route contains a subroute with two inner routes:
        //   1. HLS broadcast playlist/segments (/hls/broadcasts/{broadcastId}/*):
        //      forward_auth to the instance backend, then strip /hls prefix and
        //      proxy to MinIO for the underlying object. Mirrors the standalone
        //      xcord-fed Caddyfile HLS route so hub-provisioned instances can
        //      serve LiveKit Egress HLS output gated by broadcast auth.
        //   2. Everything else: reverse_proxy to the instance container.
        var hlsSubroute = BuildHlsSubroute(containerName);
        var appRoute = new Dictionary<string, object>
        {
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
            }
        };

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
                    ["handler"] = "subroute",
                    ["routes"] = new object[]
                    {
                        hlsSubroute,
                        appRoute
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

        // Add a TLS automation policy so Caddy provisions a cert for this domain.
        // Uses the same "internal" issuer as the hub domain (self-signed CA locally,
        // ACME in production via the Caddyfile's tls directive).
        await EnsureTlsAutomationPolicyAsync(instanceDomain, cancellationToken);

        _logger.LogInformation("Created Caddy route {RouteId} for instance {Domain}", routeId, instanceDomain);
        return routeId;
    }

    /// <summary>
    /// Builds the HLS broadcast subroute that mirrors xcord-fed's standalone
    /// Caddyfile. Matches /hls/broadcasts/{broadcastId}/* requests, uses
    /// forward_auth against the instance backend to verify the requester has
    /// access to the broadcast, then strips the /hls prefix and reverse-proxies
    /// the underlying object from MinIO.
    /// </summary>
    private static Dictionary<string, object> BuildHlsSubroute(string containerName)
    {
        // forward_auth is sugar for a reverse_proxy handler that calls an auth
        // endpoint and uses handle_response to abort on non-2xx (so the inner
        // routes below it only run when auth passes). The JSON form below is
        // what Caddy's adapter produces from the Caddyfile `forward_auth`
        // directive.
        var forwardAuth = new Dictionary<string, object>
        {
            ["handler"] = "reverse_proxy",
            ["rewrite"] = new Dictionary<string, object>
            {
                ["method"] = "GET",
                ["uri"] = "/api/v1/broadcasts/{http.regexp.broadcastPath.1}/hls-check"
            },
            ["headers"] = new Dictionary<string, object>
            {
                ["request"] = new Dictionary<string, object>
                {
                    ["set"] = new Dictionary<string, object>
                    {
                        ["Cookie"] = new[] { "{http.request.header.Cookie}" }
                    }
                }
            },
            ["upstreams"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["dial"] = $"{containerName}:80"
                }
            },
            // On any non-2xx response, abort the entire subroute (request is
            // denied). 2xx responses fall through to the next inner route
            // (strip_prefix + MinIO reverse_proxy).
            ["handle_response"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["match"] = new Dictionary<string, object>
                    {
                        ["status_code"] = new[] { 2 }
                    }
                }
            }
        };

        var stripPrefix = new Dictionary<string, object>
        {
            ["handler"] = "rewrite",
            ["strip_path_prefix"] = "/hls"
        };

        var proxyToMinio = new Dictionary<string, object>
        {
            ["handler"] = "reverse_proxy",
            ["headers"] = new Dictionary<string, object>
            {
                ["request"] = new Dictionary<string, object>
                {
                    ["set"] = new Dictionary<string, object>
                    {
                        ["Host"] = new[] { "minio:9000" }
                    }
                }
            },
            ["upstreams"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["dial"] = "minio:9000"
                }
            }
        };

        return new Dictionary<string, object>
        {
            ["match"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["path_regexp"] = new Dictionary<string, object>
                    {
                        ["name"] = "broadcastPath",
                        ["pattern"] = "^/hls/broadcasts/([^/]+)/.+$"
                    }
                }
            },
            ["handle"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["handler"] = "subroute",
                    ["routes"] = new object[]
                    {
                        new Dictionary<string, object> { ["handle"] = new[] { forwardAuth } },
                        new Dictionary<string, object> { ["handle"] = new[] { stripPrefix } },
                        new Dictionary<string, object> { ["handle"] = new[] { proxyToMinio } }
                    }
                }
            }
        };
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

    private async Task EnsureTlsAutomationPolicyAsync(string domain, CancellationToken cancellationToken)
    {
        // Append a TLS automation policy for this domain using the internal issuer.
        // This tells Caddy to provision a certificate for the domain.
        var policy = new Dictionary<string, object>
        {
            ["subjects"] = new[] { domain },
            ["issuers"] = new[]
            {
                new Dictionary<string, string>
                {
                    ["module"] = "internal"
                }
            }
        };

        var response = await _httpClient.PostAsJsonAsync(
            "/config/apps/tls/automation/policies",
            policy,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Failed to add TLS automation policy for {Domain}: {Error}", domain, error);
            // Non-fatal: in production, ACME certs may be handled differently
        }
        else
        {
            _logger.LogInformation("Added TLS automation policy for {Domain}", domain);
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
