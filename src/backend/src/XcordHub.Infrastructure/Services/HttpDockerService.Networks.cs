using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Xcord.Exceptions;

namespace XcordHub.Infrastructure.Services;

public sealed partial class HttpDockerService
{
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

        var response = await _httpClient.PostAsJsonAsync("/networks/create", payload, cancellationToken).ConfigureAwait(false);

        // 409 = network already exists (e.g. from a previous provisioning attempt) - look it up
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            _logger.LogInformation("Network {NetworkName} already exists, looking up ID", networkName);
            var inspectResponse = await _httpClient.GetAsync($"/networks/{networkName}", cancellationToken).ConfigureAwait(false);
            inspectResponse.EnsureSuccessStatusCode();
            var inspectResult = await inspectResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
            var existingId = inspectResult.GetProperty("Id").GetString()
                ?? throw new ProvisioningFailedException(
                    $"Could not resolve existing network ID for {networkName}",
                    networkName);
            _logger.LogInformation("Resolved existing network {NetworkId} for instance {Domain}", existingId, instanceDomain);
            return existingId;
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DockerNetworkCreateResponse>(cancellationToken).ConfigureAwait(false);
        if (result?.Id == null)
        {
            throw new ProvisioningFailedException(
                "Docker API returned null network ID",
                networkName);
        }

        _logger.LogInformation("Created Docker network {NetworkId} for instance {Domain}", result.Id, instanceDomain);
        return result.Id;
    }

    /// <summary>
    /// Returns true if the Docker network exists, false ONLY if the Docker API confirms it does
    /// not (HTTP 404). Transient failures (timeouts, connection drops, 5xx) propagate to the
    /// caller so the reconciler can retry on the next cycle rather than mistakenly marking a
    /// healthy instance as Failed because of a flaky Docker socket.
    /// </summary>
    public async Task<bool> VerifyNetworkAsync(string networkId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/networks/{networkId}", cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        // 404 = network definitively does not exist. This is the only "permanent" failure here.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Network {NetworkId} not found (Docker API 404)", networkId);
            return false;
        }

        // Anything else (5xx, 401, etc.) is treated as transient and surfaced so the caller can retry.
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new NetworkVerificationException(
            $"Docker API returned {(int)response.StatusCode} {response.StatusCode} verifying network {networkId}: {body}",
            networkId);
    }

    public async Task RemoveNetworkAsync(string networkId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Removing network {NetworkId}", networkId);

        var response = await _httpClient.DeleteAsync($"/networks/{networkId}", cancellationToken).ConfigureAwait(false);

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
        var response = await _httpClient.GetAsync($"/networks/{networkName}", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var networkDoc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
        return networkDoc.GetProperty("Id").GetString()
            ?? throw new ProvisioningFailedException(
                $"Could not resolve network ID for {networkName}",
                networkName);
    }
}
