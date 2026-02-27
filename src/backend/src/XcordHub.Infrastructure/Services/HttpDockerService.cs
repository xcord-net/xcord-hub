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

    // xcord-shared-net: services (postgres, redis, minio, livekit, mailpit, caddy)
    // Instance services join xcord-shared-net so they can reach those services,
    // but xcord-hub-infra-net (where docker-socket-proxy lives) is never attached
    // to instance containers — preventing a compromised instance from reaching
    // the Docker API.
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
            Driver = "overlay",
            // Attachable allows non-service containers (e.g. compose services)
            // to also join this overlay network when needed.
            Attachable = true,
            Labels = new Dictionary<string, string>
            {
                ["xcord.instance.domain"] = instanceDomain,
                ["xcord.instance.subdomain"] = subdomain
            }
        };

        _logger.LogInformation("Creating Docker overlay network {NetworkName} for instance {Domain}", networkName, instanceDomain);

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

    /// <summary>
    /// Creates a Docker secret containing the instance config JSON.
    /// Returns the secret ID, which must be passed to <see cref="StartContainerAsync"/>.
    /// The secret is mounted at /run/secrets/xcord-config inside the service container
    /// and is never exposed via <c>docker inspect</c> on the container itself.
    /// Requires Docker Swarm mode to be initialized.
    /// </summary>
    public async Task<string> CreateSecretAsync(string instanceDomain, string configJson, CancellationToken cancellationToken = default)
    {
        var subdomain = ValidationHelpers.ExtractSubdomain(instanceDomain);
        var secretName = $"xcord-{subdomain}-config";

        // Docker secrets store the data as base64-encoded in the API request
        var configBytes = Encoding.UTF8.GetBytes(configJson);
        var configBase64 = Convert.ToBase64String(configBytes);

        var payload = new
        {
            Name = secretName,
            Data = configBase64,
            Labels = new Dictionary<string, string>
            {
                ["xcord.instance.domain"] = instanceDomain,
                ["xcord.instance.subdomain"] = subdomain
            }
        };

        _logger.LogInformation("Creating Docker secret {SecretName} for instance {Domain}", secretName, instanceDomain);

        var response = await _httpClient.PostAsJsonAsync("/secrets/create", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DockerSecretCreateResponse>(cancellationToken);
        if (result?.Id == null)
        {
            throw new InvalidOperationException("Docker API returned null secret ID");
        }

        _logger.LogInformation("Created Docker secret {SecretId} for instance {Domain}", result.Id, instanceDomain);
        return result.Id;
    }

    /// <summary>
    /// Removes a Docker secret by ID. Safe to call even if the secret no longer exists.
    /// </summary>
    public async Task RemoveSecretAsync(string secretId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretId))
        {
            return;
        }

        _logger.LogInformation("Removing Docker secret {SecretId}", secretId);

        var response = await _httpClient.DeleteAsync($"/secrets/{secretId}", cancellationToken);

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Removed Docker secret {SecretId}", secretId);
    }

    /// <summary>
    /// Creates a Swarm service for the instance. Docker Swarm services support
    /// secret mounting, which keeps credentials out of <c>docker inspect</c> and
    /// <c>/proc/&lt;pid&gt;/environ</c>. Returns the service ID (stored as DockerContainerId
    /// for backward compatibility with the infrastructure record).
    /// </summary>
    public async Task<string> StartContainerAsync(string instanceDomain, string secretId, ContainerResourceLimits? resourceLimits = null, CancellationToken cancellationToken = default)
    {
        var subdomain = ValidationHelpers.ExtractSubdomain(instanceDomain);
        var serviceName = $"xcord-{subdomain}-api";
        var networkName = $"xcord-{subdomain}-net";

        // Resolve network IDs for the service spec. Swarm services reference
        // networks by ID (not name) in the TaskTemplate.
        var instanceNetworkId = await ResolveNetworkIdAsync(networkName, cancellationToken);
        var sharedNetworkId = await ResolveNetworkIdAsync(SharedNetworkName, cancellationToken);

        // Build resource limits for the service task template
        var resources = new Dictionary<string, object>();
        if (resourceLimits != null)
        {
            resources["Limits"] = new
            {
                MemoryBytes = resourceLimits.MemoryBytes,
                NanoCPUs = resourceLimits.CpuQuota * 1000 // Convert from CpuQuota (microseconds per 100ms) to NanoCPUs
            };
        }

        var servicePayload = new Dictionary<string, object>
        {
            ["Name"] = serviceName,
            ["Labels"] = new Dictionary<string, string>
            {
                ["xcord.instance.domain"] = instanceDomain,
                ["xcord.instance.subdomain"] = subdomain,
                ["xcord.instance.type"] = "api"
            },
            ["TaskTemplate"] = new Dictionary<string, object>
            {
                ["ContainerSpec"] = new Dictionary<string, object>
                {
                    ["Image"] = _instanceImage,
                    ["Hostname"] = serviceName,
                    ["Env"] = new[] { "ASPNETCORE_ENVIRONMENT=Production" },
                    ["Secrets"] = new[]
                    {
                        new
                        {
                            SecretID = secretId,
                            SecretName = $"xcord-{subdomain}-config",
                            File = new
                            {
                                Name = "xcord-config",
                                UID = "0",
                                GID = "0",
                                Mode = 292u // 0444 octal
                            }
                        }
                    }
                },
                ["Networks"] = new[]
                {
                    new { Target = instanceNetworkId },
                    new { Target = sharedNetworkId }
                },
                ["Resources"] = resources.Count > 0 ? resources : new Dictionary<string, object>(),
                ["RestartPolicy"] = new
                {
                    Condition = "on-failure",
                    MaxAttempts = 3L
                }
            },
            ["Mode"] = new
            {
                Replicated = new { Replicas = 1L }
            }
        };

        _logger.LogInformation("Creating Swarm service {ServiceName} for instance {Domain}", serviceName, instanceDomain);

        var createResponse = await _httpClient.PostAsJsonAsync("/services/create", servicePayload, cancellationToken);
        createResponse.EnsureSuccessStatusCode();

        var createResult = await createResponse.Content.ReadFromJsonAsync<DockerServiceCreateResponse>(cancellationToken);
        if (createResult?.ID == null)
        {
            throw new InvalidOperationException("Docker API returned null service ID");
        }

        _logger.LogInformation("Created Swarm service {ServiceId} for instance {Domain}", createResult.ID, instanceDomain);
        return createResult.ID;
    }

    /// <summary>
    /// Verifies that the Swarm service has at least one running task.
    /// Polls for up to 60 seconds because Swarm task scheduling adds latency
    /// compared to starting a plain container.
    /// The <paramref name="containerId"/> here is actually the service ID
    /// (stored in <c>Infrastructure.DockerContainerId</c>).
    /// </summary>
    public async Task<bool> VerifyContainerRunningAsync(string containerId, CancellationToken cancellationToken = default)
    {
        const int maxWaitMs = 60_000;
        const int pollIntervalMs = 3_000;
        var serviceId = containerId;
        var elapsed = 0;

        while (elapsed < maxWaitMs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var response = await _httpClient.GetAsync(
                    $"/tasks?filters={{\"service\":[\"{serviceId}\"],\"desired-state\":[\"running\"]}}",
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var tasks = await response.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken);
                    if (tasks != null)
                    {
                        foreach (var task in tasks)
                        {
                            if (task.TryGetProperty("Status", out var status) &&
                                status.TryGetProperty("State", out var state) &&
                                state.GetString() == "running")
                            {
                                _logger.LogInformation("Service {ServiceId} task is running after {Elapsed}ms", serviceId, elapsed);
                                return true;
                            }

                            // Check for terminal failure states
                            if (task.TryGetProperty("Status", out var failStatus) &&
                                failStatus.TryGetProperty("State", out var failState))
                            {
                                var stateStr = failState.GetString();
                                if (stateStr is "failed" or "rejected" or "shutdown")
                                {
                                    var errMsg = failStatus.TryGetProperty("Err", out var err) ? err.GetString() : "unknown error";
                                    _logger.LogWarning("Service {ServiceId} task in terminal state {State}: {Error}",
                                        serviceId, stateStr, errMsg);
                                    return false;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error polling service {ServiceId} tasks", serviceId);
            }

            await Task.Delay(pollIntervalMs, cancellationToken);
            elapsed += pollIntervalMs;
        }

        _logger.LogWarning("Service {ServiceId} did not reach running state within {MaxWait}s", serviceId, maxWaitMs / 1000);
        return false;
    }

    /// <summary>
    /// Runs database migrations using a one-shot Swarm service with a Docker secret.
    /// The service is configured with <c>restart-condition: none</c> so it exits
    /// after the migration completes.
    /// </summary>
    public async Task RunMigrationContainerAsync(string instanceDomain, string configJson, CancellationToken cancellationToken = default)
    {
        var subdomain = ValidationHelpers.ExtractSubdomain(instanceDomain);
        var serviceName = $"xcord-{subdomain}-migrations";
        var networkName = $"xcord-{subdomain}-net";

        // Create a temporary secret for the migration service
        var secretId = await CreateSecretAsync(instanceDomain + ".migrations", configJson, cancellationToken);

        try
        {
            var instanceNetworkId = await ResolveNetworkIdAsync(networkName, cancellationToken);
            var sharedNetworkId = await ResolveNetworkIdAsync(SharedNetworkName, cancellationToken);

            var servicePayload = new Dictionary<string, object>
            {
                ["Name"] = serviceName,
                ["Labels"] = new Dictionary<string, string>
                {
                    ["xcord.instance.domain"] = instanceDomain,
                    ["xcord.instance.subdomain"] = subdomain,
                    ["xcord.instance.type"] = "migrations"
                },
                ["TaskTemplate"] = new Dictionary<string, object>
                {
                    ["ContainerSpec"] = new Dictionary<string, object>
                    {
                        ["Image"] = _instanceImage,
                        ["Hostname"] = serviceName,
                        ["Env"] = new[] { "ASPNETCORE_ENVIRONMENT=Production" },
                        ["Command"] = new[] { "dotnet", "Xcord.Api.dll", "--migrate" },
                        ["Secrets"] = new[]
                        {
                            new
                            {
                                SecretID = secretId,
                                SecretName = $"xcord-{subdomain}.migrations-config",
                                File = new
                                {
                                    Name = "xcord-config",
                                    UID = "0",
                                    GID = "0",
                                    Mode = 292u // 0444 octal
                                }
                            }
                        }
                    },
                    ["Networks"] = new[]
                    {
                        new { Target = instanceNetworkId },
                        new { Target = sharedNetworkId }
                    },
                    ["RestartPolicy"] = new
                    {
                        Condition = "none"
                    }
                },
                ["Mode"] = new
                {
                    Replicated = new { Replicas = 1L }
                }
            };

            _logger.LogInformation("Creating migration service {ServiceName} for instance {Domain}", serviceName, instanceDomain);

            var createResponse = await _httpClient.PostAsJsonAsync("/services/create", servicePayload, cancellationToken);
            createResponse.EnsureSuccessStatusCode();

            var createResult = await createResponse.Content.ReadFromJsonAsync<DockerServiceCreateResponse>(cancellationToken);
            if (createResult?.ID == null)
            {
                throw new InvalidOperationException("Docker API returned null service ID for migrations");
            }

            var serviceId = createResult.ID;

            _logger.LogInformation("Started migration service {ServiceId} for instance {Domain}", serviceId, instanceDomain);

            // Wait for the migration task to complete (poll for task state)
            await WaitForServiceTaskCompletionAsync(serviceId, cancellationToken);

            // Remove the migration service
            var deleteResponse = await _httpClient.DeleteAsync($"/services/{serviceId}", cancellationToken);
            if (!deleteResponse.IsSuccessStatusCode && deleteResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                deleteResponse.EnsureSuccessStatusCode();
            }

            _logger.LogInformation("Migration service {ServiceId} completed successfully", serviceId);
        }
        finally
        {
            // Always clean up the temporary migration secret
            await RemoveSecretAsync(secretId, cancellationToken);
        }
    }

    public async Task<bool> VerifyMigrationsCompleteAsync(string instanceDomain, CancellationToken cancellationToken = default)
    {
        // Migrations are verified by successful completion of RunMigrationContainerAsync
        await Task.CompletedTask;
        return true;
    }

    /// <summary>
    /// Stops a Swarm service by scaling it to 0 replicas.
    /// The <paramref name="containerId"/> is actually the service ID.
    /// </summary>
    public async Task StopContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        var serviceId = containerId;
        _logger.LogInformation("Scaling down service {ServiceId} to 0 replicas", serviceId);

        // Get the current service spec (needed for update)
        var inspectResponse = await _httpClient.GetAsync($"/services/{serviceId}", cancellationToken);
        if (!inspectResponse.IsSuccessStatusCode)
        {
            if (inspectResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Service {ServiceId} not found, skipping stop", serviceId);
                return;
            }
            inspectResponse.EnsureSuccessStatusCode();
        }

        var serviceDoc = await inspectResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var version = serviceDoc.GetProperty("Version").GetProperty("Index").GetInt64();

        // Update the service to 0 replicas
        var spec = serviceDoc.GetProperty("Spec");
        var specJson = spec.GetRawText();
        var specDict = JsonSerializer.Deserialize<Dictionary<string, object>>(specJson)!;

        // Override the Mode to 0 replicas
        specDict["Mode"] = new Dictionary<string, object>
        {
            ["Replicated"] = new { Replicas = 0L }
        };

        var updateResponse = await _httpClient.PostAsJsonAsync(
            $"/services/{serviceId}/update?version={version}",
            specDict, cancellationToken);

        if (!updateResponse.IsSuccessStatusCode && updateResponse.StatusCode != System.Net.HttpStatusCode.NotModified)
        {
            updateResponse.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Scaled service {ServiceId} to 0 replicas", serviceId);
    }

    /// <summary>
    /// Removes a Swarm service entirely.
    /// The <paramref name="containerId"/> is actually the service ID.
    /// </summary>
    public async Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        var serviceId = containerId;
        _logger.LogInformation("Removing service {ServiceId}", serviceId);

        var response = await _httpClient.DeleteAsync($"/services/{serviceId}", cancellationToken);

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Removed service {ServiceId}", serviceId);
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

    /// <summary>
    /// Resolves a network name to its ID, required for Swarm service network references.
    /// </summary>
    private async Task<string> ResolveNetworkIdAsync(string networkName, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"/networks/{networkName}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var networkDoc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return networkDoc.GetProperty("Id").GetString()
            ?? throw new InvalidOperationException($"Could not resolve network ID for {networkName}");
    }

    /// <summary>
    /// Polls until the service's single task reaches a terminal state (complete or failed).
    /// </summary>
    private async Task WaitForServiceTaskCompletionAsync(string serviceId, CancellationToken cancellationToken)
    {
        const int maxWaitMs = 120_000;
        const int pollIntervalMs = 2_000;
        var elapsed = 0;

        while (elapsed < maxWaitMs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await _httpClient.GetAsync(
                $"/tasks?filters={{\"service\":[\"{serviceId}\"]}}",
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var tasks = await response.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken);
                if (tasks != null && tasks.Length > 0)
                {
                    // Check the most recent task
                    var latestTask = tasks[^1];
                    if (latestTask.TryGetProperty("Status", out var status) &&
                        status.TryGetProperty("State", out var state))
                    {
                        var stateStr = state.GetString();
                        switch (stateStr)
                        {
                            case "complete":
                                return;
                            case "failed":
                            case "rejected":
                                var errMsg = status.TryGetProperty("Err", out var err) ? err.GetString() : "unknown error";
                                throw new InvalidOperationException($"Migration task failed: {errMsg}");
                        }
                    }
                }
            }

            await Task.Delay(pollIntervalMs, cancellationToken);
            elapsed += pollIntervalMs;
        }

        throw new TimeoutException($"Migration service {serviceId} did not complete within {maxWaitMs / 1000}s");
    }

    private sealed class DockerNetworkCreateResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Warning { get; set; } = string.Empty;
    }

    private sealed class DockerSecretCreateResponse
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class DockerServiceCreateResponse
    {
        public string ID { get; set; } = string.Empty;
    }
}
