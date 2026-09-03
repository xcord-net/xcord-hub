using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Xcord.Exceptions;

namespace XcordHub.Infrastructure.Services;

public sealed partial class HttpDockerService
{
    /// <summary>
    /// Creates a Swarm service for the instance. Docker Swarm services support
    /// secret mounting, which keeps credentials out of <c>docker inspect</c> and
    /// <c>/proc/&lt;pid&gt;/environ</c>. Returns the service ID (stored as DockerContainerId
    /// for backward compatibility with the infrastructure record).
    /// </summary>
    public async Task<string> StartContainerAsync(string instanceDomain, string configSecretId, string? kekSecretId = null, ContainerResourceLimits? resourceLimits = null, string? poolNetworkName = null, CancellationToken cancellationToken = default)
    {
        var subdomain = ValidationHelpers.ExtractSubdomain(instanceDomain);
        var serviceName = $"xcord-{subdomain}-api";
        var networkName = $"xcord-{subdomain}-net";

        // Resolve network IDs for the service spec. Swarm services reference
        // networks by ID (not name) in the TaskTemplate.
        // In production, use the pool-specific overlay network (xcord-pool-{name}) so instances
        // on different pools are isolated. In dev, fall back to the shared network.
        var infraNetworkName = !string.IsNullOrWhiteSpace(poolNetworkName) ? poolNetworkName : SharedNetworkName;
        var instanceNetworkId = await ResolveNetworkIdAsync(networkName, cancellationToken).ConfigureAwait(false);
        var infraNetworkId = await ResolveNetworkIdAsync(infraNetworkName, cancellationToken).ConfigureAwait(false);

        // Build resource limits for the service task template
        var resources = new Dictionary<string, object>();
        if (resourceLimits != null)
        {
            resources["Limits"] = new
            {
                MemoryBytes = resourceLimits.MemoryBytes,
                NanoCPUs = resourceLimits.CpuQuota * 10_000 // 1 CPU = 1e9 NanoCPUs; CpuQuota is µs per 100ms → NanoCPUs = CpuQuota / 100_000 * 1e9
            };
        }

        // Build secret mounts: config secret (readable) + optional KEK secret (restricted)
        var secrets = BuildSecretMounts(configSecretId, $"xcord-{subdomain}-config", kekSecretId, $"xcord-{subdomain}-kek");

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
                    ["Env"] = new[] { $"ASPNETCORE_ENVIRONMENT={_instanceEnvironment}" },
                    ["Secrets"] = secrets
                },
                ["Networks"] = new[]
                {
                    new { Target = instanceNetworkId },
                    new { Target = infraNetworkId }
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

        var createResponse = await _httpClient.PostAsJsonAsync("/services/create", servicePayload, cancellationToken).ConfigureAwait(false);
        createResponse.EnsureSuccessStatusCode();

        var createResult = await createResponse.Content.ReadFromJsonAsync<DockerServiceCreateResponse>(cancellationToken).ConfigureAwait(false);
        if (createResult?.ID == null)
        {
            throw new ProvisioningFailedException(
                "Docker API returned null service ID",
                serviceName);
        }

        _logger.LogInformation("Created Swarm service {ServiceId} for instance {Domain}", createResult.ID, instanceDomain);
        return createResult.ID;
    }

    /// <summary>
    /// Verifies that the Swarm service has at least one running task, polling
    /// until one does or the deadline passes.
    /// The <paramref name="containerId"/> here is actually the service ID
    /// (stored in <c>Infrastructure.DockerContainerId</c>).
    /// </summary>
    /// <remarks>
    /// A single task in a terminal state used to end the wait immediately, and
    /// that is wrong on the path this is most often called from. Resuming a
    /// service scales it 0 -> 1, and for a moment afterwards Swarm still lists
    /// the task it shut down on the way to zero alongside the one it is
    /// starting. Reading the shut-down one first returned false while the
    /// replacement was still coming up - so a resume failed or succeeded
    /// depending on the order Swarm happened to return two tasks in. A service
    /// that crashes and is being retried has the same shape.
    ///
    /// A terminal task is therefore only evidence of failure when it is the
    /// whole picture: no task running, and every task terminal, seen on
    /// consecutive polls rather than once.
    /// </remarks>
    public async Task<bool> VerifyContainerRunningAsync(string containerId, CancellationToken cancellationToken = default)
    {
        const int maxWaitMs = 15_000;
        const int pollIntervalMs = 1_000;
        // One poll can catch the gap between the old task going away and the new
        // one being created. Three consecutive says the service really is dead.
        const int terminalPollsBeforeGivingUp = 3;

        var serviceId = containerId;
        var elapsed = 0;
        var allTerminalStreak = 0;
        string? lastTerminalError = null;

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
                    var tasks = await response.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken).ConfigureAwait(false);
                    if (tasks is { Length: > 0 })
                    {
                        var running = false;
                        var terminal = 0;

                        foreach (var task in tasks)
                        {
                            if (!task.TryGetProperty("Status", out var status) ||
                                !status.TryGetProperty("State", out var state))
                            {
                                continue;
                            }

                            var stateStr = state.GetString();
                            if (stateStr == "running")
                            {
                                running = true;
                                break;
                            }

                            if (stateStr is "failed" or "rejected" or "shutdown" or "orphaned")
                            {
                                terminal++;
                                lastTerminalError = status.TryGetProperty("Err", out var err)
                                    ? err.GetString()
                                    : $"task {stateStr}";
                            }
                        }

                        if (running)
                        {
                            _logger.LogInformation("Service {ServiceId} task is running after {Elapsed}ms", serviceId, elapsed);
                            return true;
                        }

                        if (terminal == tasks.Length)
                        {
                            allTerminalStreak++;
                            if (allTerminalStreak >= terminalPollsBeforeGivingUp)
                            {
                                _logger.LogWarning(
                                    "Service {ServiceId} has no running task and all {Count} task(s) are terminal: {Error}",
                                    serviceId, tasks.Length, lastTerminalError ?? "unknown error");
                                return false;
                            }
                        }
                        else
                        {
                            allTerminalStreak = 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error polling service {ServiceId} tasks", serviceId);
            }

            await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
            elapsed += pollIntervalMs;
        }

        _logger.LogWarning("Service {ServiceId} did not reach running state within {MaxWait}s (last task error: {Error})",
            serviceId, maxWaitMs / 1000, lastTerminalError ?? "none");
        return false;
    }

    public async Task<bool> VerifyServiceExistsAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/services/{serviceId}", cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Runs database migrations using a one-shot Swarm service with a Docker secret.
    /// The service is configured with <c>restart-condition: none</c> so it exits
    /// after the migration completes.
    /// </summary>
    public async Task RunMigrationContainerAsync(string instanceDomain, string configJson, string? kekSecretId = null, string? poolNetworkName = null, CancellationToken cancellationToken = default)
    {
        var subdomain = ValidationHelpers.ExtractSubdomain(instanceDomain);
        var serviceName = $"xcord-{subdomain}-migrations";
        var networkName = $"xcord-{subdomain}-net";

        // Create a temporary secret for the migration service
        var configSecretId = await CreateSecretAsync(instanceDomain + ".migrations", configJson, cancellationToken).ConfigureAwait(false);

        try
        {
            // In production, use the pool-specific overlay network so migration containers
            // are also isolated to their pool. Fall back to shared network in dev mode.
            var infraNetworkName = !string.IsNullOrWhiteSpace(poolNetworkName) ? poolNetworkName : SharedNetworkName;
            var instanceNetworkId = await ResolveNetworkIdAsync(networkName, cancellationToken).ConfigureAwait(false);
            var infraNetworkId = await ResolveNetworkIdAsync(infraNetworkName, cancellationToken).ConfigureAwait(false);

            // Build secret mounts: config secret + optional KEK secret (reused from provisioning)
            var secrets = BuildSecretMounts(configSecretId, $"xcord-{subdomain}.migrations-config", kekSecretId, $"xcord-{subdomain}-kek");

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
                        ["Env"] = new[] { $"ASPNETCORE_ENVIRONMENT={_instanceEnvironment}" },
                        ["Command"] = new[] { "dotnet", "Xcord.Api.dll", "--migrate" },
                        ["Secrets"] = secrets
                    },
                    ["Networks"] = new[]
                    {
                        new { Target = instanceNetworkId },
                        new { Target = infraNetworkId }
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

            var createResponse = await _httpClient.PostAsJsonAsync("/services/create", servicePayload, cancellationToken).ConfigureAwait(false);
            createResponse.EnsureSuccessStatusCode();

            var createResult = await createResponse.Content.ReadFromJsonAsync<DockerServiceCreateResponse>(cancellationToken).ConfigureAwait(false);
            if (createResult?.ID == null)
            {
                throw new ProvisioningFailedException(
                    "Docker API returned null service ID for migrations",
                    serviceName);
            }

            var serviceId = createResult.ID;

            _logger.LogInformation("Started migration service {ServiceId} for instance {Domain}", serviceId, instanceDomain);

            // Wait for the migration task to complete (poll for task state)
            await WaitForServiceTaskCompletionAsync(serviceId, cancellationToken).ConfigureAwait(false);

            // Remove the migration service
            var deleteResponse = await _httpClient.DeleteAsync($"/services/{serviceId}", cancellationToken).ConfigureAwait(false);
            if (!deleteResponse.IsSuccessStatusCode && deleteResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                deleteResponse.EnsureSuccessStatusCode();
            }

            _logger.LogInformation("Migration service {ServiceId} completed successfully", serviceId);
        }
        finally
        {
            // Always clean up the temporary migration config secret (KEK secret is persistent)
            await RemoveSecretAsync(configSecretId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> VerifyMigrationsCompleteAsync(string instanceDomain, CancellationToken cancellationToken = default)
    {
        // Migrations are verified by successful completion of RunMigrationContainerAsync
        await Task.CompletedTask;
        return true;
    }

    /// <summary>
    /// Updates a Swarm service to use a new Docker image.
    /// Swarm handles the container replacement (stop old, start new).
    /// </summary>
    public async Task UpdateServiceImageAsync(string serviceId, string newImage, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating service {ServiceId} to image {Image}", serviceId, newImage);

        var inspectResponse = await _httpClient.GetAsync($"/services/{serviceId}", cancellationToken).ConfigureAwait(false);
        if (!inspectResponse.IsSuccessStatusCode)
        {
            inspectResponse.EnsureSuccessStatusCode();
        }

        var serviceDoc = await inspectResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
        var version = serviceDoc.GetProperty("Version").GetProperty("Index").GetInt64();

        // Clone the existing spec and update the image
        var spec = serviceDoc.GetProperty("Spec");
        using var specStream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(specStream))
        {
            WriteSpecWithNewImage(spec, newImage, writer);
        }

        specStream.Position = 0;
        var content = new StreamContent(specStream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var updateResponse = await _httpClient.PostAsync(
            $"/services/{serviceId}/update?version={version}",
            content, cancellationToken);

        if (!updateResponse.IsSuccessStatusCode && updateResponse.StatusCode != System.Net.HttpStatusCode.NotModified)
        {
            var body = await updateResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("Failed to update service {ServiceId}: {StatusCode} {Body}", serviceId, updateResponse.StatusCode, body);
            updateResponse.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Updated service {ServiceId} to image {Image}", serviceId, newImage);
    }

    /// <summary>
    /// Deep-clones a service spec JSON, replacing only the container image.
    /// </summary>
    private static void WriteSpecWithNewImage(JsonElement spec, string newImage, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        foreach (var prop in spec.EnumerateObject())
        {
            if (prop.Name == "TaskTemplate")
            {
                writer.WritePropertyName("TaskTemplate");
                writer.WriteStartObject();
                foreach (var ttProp in prop.Value.EnumerateObject())
                {
                    if (ttProp.Name == "ContainerSpec")
                    {
                        writer.WritePropertyName("ContainerSpec");
                        writer.WriteStartObject();
                        foreach (var csProp in ttProp.Value.EnumerateObject())
                        {
                            if (csProp.Name == "Image")
                            {
                                writer.WriteString("Image", newImage);
                            }
                            else
                            {
                                csProp.WriteTo(writer);
                            }
                        }
                        writer.WriteEndObject();
                    }
                    else
                    {
                        ttProp.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            else
            {
                prop.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
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
        var inspectResponse = await _httpClient.GetAsync($"/services/{serviceId}", cancellationToken).ConfigureAwait(false);
        if (!inspectResponse.IsSuccessStatusCode)
        {
            if (inspectResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Service {ServiceId} not found, skipping stop", serviceId);
                return;
            }
            inspectResponse.EnsureSuccessStatusCode();
        }

        var serviceDoc = await inspectResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
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
    /// Scales a service back to a single replica. The counterpart to
    /// <see cref="StopContainerAsync"/>, which scales it to zero.
    /// The <paramref name="containerId"/> is actually the service ID.
    /// </summary>
    public async Task StartExistingContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        var serviceId = containerId;
        _logger.LogInformation("Scaling up service {ServiceId} to 1 replica", serviceId);

        var inspectResponse = await _httpClient.GetAsync($"/services/{serviceId}", cancellationToken).ConfigureAwait(false);
        if (!inspectResponse.IsSuccessStatusCode)
        {
            if (inspectResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Service {ServiceId} not found, cannot start", serviceId);
                return;
            }
            inspectResponse.EnsureSuccessStatusCode();
        }

        var serviceDoc = await inspectResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
        var version = serviceDoc.GetProperty("Version").GetProperty("Index").GetInt64();

        var spec = serviceDoc.GetProperty("Spec");
        var specDict = JsonSerializer.Deserialize<Dictionary<string, object>>(spec.GetRawText())!;

        specDict["Mode"] = new Dictionary<string, object>
        {
            ["Replicated"] = new { Replicas = 1L }
        };

        var updateResponse = await _httpClient.PostAsJsonAsync(
            $"/services/{serviceId}/update?version={version}",
            specDict, cancellationToken);

        if (!updateResponse.IsSuccessStatusCode && updateResponse.StatusCode != System.Net.HttpStatusCode.NotModified)
        {
            updateResponse.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Scaled service {ServiceId} to 1 replica", serviceId);
    }

    /// <summary>
    /// Removes a Swarm service entirely.
    /// The <paramref name="containerId"/> is actually the service ID.
    /// </summary>
    public async Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        var serviceId = containerId;
        _logger.LogInformation("Removing service {ServiceId}", serviceId);

        var response = await _httpClient.DeleteAsync($"/services/{serviceId}", cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Removed service {ServiceId}", serviceId);
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
                var tasks = await response.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken).ConfigureAwait(false);
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
                                throw new ProvisioningFailedException($"Migration task failed: {errMsg}");
                        }
                    }
                }
            }

            await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
            elapsed += pollIntervalMs;
        }

        throw new ProvisioningFailedException(
            $"Migration service {serviceId} did not complete within {maxWaitMs / 1000}s",
            new TimeoutException($"Migration service {serviceId} did not complete within {maxWaitMs / 1000}s"));
    }
}
