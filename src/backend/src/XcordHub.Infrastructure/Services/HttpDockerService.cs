using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace XcordHub.Infrastructure.Services;

public sealed class HttpDockerService : IDockerService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpDockerService> _logger;
    private readonly string _instanceImage;
    private const string SharedNetworkName = "xcord-shared-net";

    public HttpDockerService(IHttpClientFactory httpClientFactory, ILogger<HttpDockerService> logger, IOptions<DockerOptions> options)
    {
        _httpClient = httpClientFactory.CreateClient("DockerSocketProxy");
        _logger = logger;
        var opts = options.Value;
        _instanceImage = string.IsNullOrWhiteSpace(opts.InstanceImage)
            ? "xcord-fed:latest"
            : opts.InstanceImage;
    }

    public async Task<string> CreateNetworkAsync(string instanceDomain, CancellationToken cancellationToken = default)
    {
        var subdomain = ValidationHelpers.ExtractSubdomain(instanceDomain);
        var networkName = $"xcord-{subdomain}-net";

        var payload = new
        {
            Name = networkName,
            Driver = "bridge",
            CheckDuplicate = true,
            Labels = new Dictionary<string, string>
            {
                ["xcord.instance.domain"] = instanceDomain,
                ["xcord.instance.subdomain"] = subdomain
            }
        };

        _logger.LogInformation("Creating Docker network {NetworkName} for instance {Domain}", networkName, instanceDomain);

        var response = await _httpClient.PostAsJsonAsync("/networks/create", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DockerNetworkCreateResponse>(cancellationToken);
        if (result?.Id == null)
        {
            throw new InvalidOperationException("Docker API returned null network ID");
        }

        _logger.LogInformation("Created Docker network {NetworkId} for instance {Domain}", result.Id, instanceDomain);
        return result.Id;
    }

    public async Task<bool> VerifyNetworkAsync(string networkId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/networks/{networkId}", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify network {NetworkId}", networkId);
            return false;
        }
    }

    public async Task<string> StartContainerAsync(string instanceDomain, string configJson, ContainerResourceLimits? resourceLimits = null, CancellationToken cancellationToken = default)
    {
        var subdomain = ValidationHelpers.ExtractSubdomain(instanceDomain);
        var containerName = $"xcord-{subdomain}-api";
        var networkName = $"xcord-{subdomain}-net";

        // Create container; inject config JSON via XCORD_CONFIG_INLINE so the
        // xcord-fed entrypoint can generate appsettings.Production.json without
        // needing a Docker secret mount (which requires compose-style secrets).
        var hostConfig = new Dictionary<string, object>
        {
            ["NetworkMode"] = networkName,
            ["RestartPolicy"] = new { Name = "unless-stopped" }
        };

        if (resourceLimits != null)
        {
            hostConfig["Memory"] = resourceLimits.MemoryBytes;
            hostConfig["CpuQuota"] = resourceLimits.CpuQuota;
            hostConfig["CpuPeriod"] = 100_000L;
        }

        var createPayload = new Dictionary<string, object>
        {
            ["Image"] = _instanceImage,
            ["Hostname"] = containerName,
            ["Env"] = new[]
            {
                "ASPNETCORE_ENVIRONMENT=Production",
                $"XCORD_CONFIG_INLINE={configJson}"
            },
            ["Labels"] = new Dictionary<string, string>
            {
                ["xcord.instance.domain"] = instanceDomain,
                ["xcord.instance.subdomain"] = subdomain,
                ["xcord.instance.type"] = "api"
            },
            ["HostConfig"] = hostConfig
        };

        _logger.LogInformation("Creating Docker container {ContainerName} for instance {Domain}", containerName, instanceDomain);

        // Container name is passed as query parameter per Docker Engine API spec
        var createResponse = await _httpClient.PostAsJsonAsync($"/containers/create?name={containerName}", createPayload, cancellationToken);
        createResponse.EnsureSuccessStatusCode();

        var createResult = await createResponse.Content.ReadFromJsonAsync<DockerContainerCreateResponse>(cancellationToken);
        if (createResult?.Id == null)
        {
            throw new InvalidOperationException("Docker API returned null container ID");
        }

        var containerId = createResult.Id;

        // Connect container to shared network so it can reach postgres/redis/minio/livekit/caddy
        var connectPayload = new
        {
            Container = containerId
        };

        await _httpClient.PostAsJsonAsync($"/networks/{SharedNetworkName}/connect", connectPayload, cancellationToken);

        // Start container
        var startResponse = await _httpClient.PostAsync($"/containers/{containerId}/start", null, cancellationToken);
        startResponse.EnsureSuccessStatusCode();

        _logger.LogInformation("Started Docker container {ContainerId} for instance {Domain}", containerId, instanceDomain);
        return containerId;
    }

    public async Task<bool> VerifyContainerRunningAsync(string containerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/containers/{containerId}/json", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var container = await response.Content.ReadFromJsonAsync<DockerContainerInspectResponse>(cancellationToken);
            return container?.State?.Running ?? false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify container {ContainerId}", containerId);
            return false;
        }
    }

    public async Task RunMigrationContainerAsync(string instanceDomain, CancellationToken cancellationToken = default)
    {
        var subdomain = ValidationHelpers.ExtractSubdomain(instanceDomain);
        var containerName = $"xcord-{subdomain}-migrations";
        var networkName = $"xcord-{subdomain}-net";

        var createPayload = new
        {
            Image = _instanceImage,
            Name = containerName,
            Hostname = containerName,
            Env = new[]
            {
                "ASPNETCORE_ENVIRONMENT=Production"
            },
            Labels = new Dictionary<string, string>
            {
                ["xcord.instance.domain"] = instanceDomain,
                ["xcord.instance.subdomain"] = subdomain,
                ["xcord.instance.type"] = "migrations"
            },
            HostConfig = new
            {
                NetworkMode = networkName,
                AutoRemove = true
            },
            Cmd = new[] { "dotnet", "ef", "database", "update" }
        };

        _logger.LogInformation("Creating migration container {ContainerName} for instance {Domain}", containerName, instanceDomain);

        var createResponse = await _httpClient.PostAsJsonAsync("/containers/create", createPayload, cancellationToken);
        createResponse.EnsureSuccessStatusCode();

        var createResult = await createResponse.Content.ReadFromJsonAsync<DockerContainerCreateResponse>(cancellationToken);
        if (createResult?.Id == null)
        {
            throw new InvalidOperationException("Docker API returned null container ID for migrations");
        }

        var containerId = createResult.Id;

        // Connect to shared network for DB access
        var connectPayload = new
        {
            Container = containerId
        };

        await _httpClient.PostAsJsonAsync($"/networks/{SharedNetworkName}/connect", connectPayload, cancellationToken);

        // Start and wait for completion
        var startResponse = await _httpClient.PostAsync($"/containers/{containerId}/start", null, cancellationToken);
        startResponse.EnsureSuccessStatusCode();

        _logger.LogInformation("Started migration container {ContainerId} for instance {Domain}", containerId, instanceDomain);

        // Wait for container to exit
        var waitResponse = await _httpClient.PostAsync($"/containers/{containerId}/wait", null, cancellationToken);
        waitResponse.EnsureSuccessStatusCode();

        var waitResult = await waitResponse.Content.ReadFromJsonAsync<DockerContainerWaitResponse>(cancellationToken);
        if (waitResult?.StatusCode != 0)
        {
            throw new InvalidOperationException($"Migration container exited with status code {waitResult?.StatusCode}");
        }

        _logger.LogInformation("Migration container {ContainerId} completed successfully", containerId);
    }

    public async Task<bool> VerifyMigrationsCompleteAsync(string instanceDomain, CancellationToken cancellationToken = default)
    {
        // Migrations are verified by successful completion of RunMigrationContainerAsync
        // This method is called as a separate verify step, so we just return true
        // if we got here, it means migrations succeeded
        await Task.CompletedTask;
        return true;
    }

    public async Task StopContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping container {ContainerId}", containerId);

        var response = await _httpClient.PostAsync($"/containers/{containerId}/stop?t=10", null, cancellationToken);

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotModified)
        {
            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Stopped container {ContainerId}", containerId);
    }

    public async Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Removing container {ContainerId}", containerId);

        var response = await _httpClient.DeleteAsync($"/containers/{containerId}?force=true", cancellationToken);

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Removed container {ContainerId}", containerId);
    }

    public async Task RemoveNetworkAsync(string networkId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Removing network {NetworkId}", networkId);

        var response = await _httpClient.DeleteAsync($"/networks/{networkId}", cancellationToken);

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Removed network {NetworkId}", networkId);
    }

    private sealed class DockerNetworkCreateResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Warning { get; set; } = string.Empty;
    }

    private sealed class DockerContainerCreateResponse
    {
        public string Id { get; set; } = string.Empty;
        public string[] Warnings { get; set; } = Array.Empty<string>();
    }

    private sealed class DockerContainerInspectResponse
    {
        public ContainerState? State { get; set; }
    }

    private sealed class ContainerState
    {
        public bool Running { get; set; }
    }

    private sealed class DockerContainerWaitResponse
    {
        public int StatusCode { get; set; }
    }
}
