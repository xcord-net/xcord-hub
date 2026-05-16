using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Xcord.Exceptions;

namespace XcordHub.Infrastructure.Services;

public sealed partial class HttpDockerService
{
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

        var response = await _httpClient.PostAsJsonAsync("/secrets/create", payload, cancellationToken).ConfigureAwait(false);

        // Handle 409 Conflict: secret already exists (provisioning retry after partial failure).
        // Look up the existing secret by name and return its ID.
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            _logger.LogInformation("Secret {SecretName} already exists, looking up existing ID", secretName);
            var lookupResponse = await _httpClient.GetAsync(
                $"/secrets?filters=%7B%22name%22%3A%5B%22{secretName}%22%5D%7D", cancellationToken);
            lookupResponse.EnsureSuccessStatusCode();
            var secrets = await lookupResponse.Content.ReadFromJsonAsync<List<DockerSecretListItem>>(cancellationToken).ConfigureAwait(false);
            var existing = secrets?.FirstOrDefault(s => s.Spec?.Name == secretName);
            if (existing?.ID != null)
            {
                _logger.LogInformation("Found existing secret {SecretId} for {SecretName}", existing.ID, secretName);
                return existing.ID;
            }
            throw new ProvisioningFailedException(
                $"Secret '{secretName}' conflict but could not find existing secret",
                secretName);
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DockerSecretCreateResponse>(cancellationToken).ConfigureAwait(false);
        if (result?.Id == null)
        {
            throw new ProvisioningFailedException(
                "Docker API returned null secret ID",
                secretName);
        }

        _logger.LogInformation("Created Docker secret {SecretId} for instance {Domain}", result.Id, instanceDomain);
        return result.Id;
    }

    /// <summary>
    /// Creates a Docker secret with an explicit name and raw string data.
    /// Used for secrets like the instance KEK that are not tied to config JSON.
    /// </summary>
    public async Task<string> CreateRawSecretAsync(string secretName, string data, CancellationToken cancellationToken = default)
    {
        var dataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(data));

        var payload = new
        {
            Name = secretName,
            Data = dataBase64,
            Labels = new Dictionary<string, string>
            {
                ["xcord.secret.type"] = "raw"
            }
        };

        _logger.LogInformation("Creating Docker secret {SecretName}", secretName);

        var response = await _httpClient.PostAsJsonAsync("/secrets/create", payload, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Secret already exists (e.g. from a previous provisioning run).
            // Look up its ID by name and reuse it - KEK secrets persist across
            // container recreations and should not be regenerated.
            _logger.LogInformation("Docker secret {SecretName} already exists, looking up ID", secretName);
            var existingId = await GetSecretIdByNameAsync(secretName, cancellationToken).ConfigureAwait(false);
            if (existingId == null)
                throw new ProvisioningFailedException(
                    $"Docker secret '{secretName}' reported as conflict but not found by name",
                    secretName);
            return existingId;
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DockerSecretCreateResponse>(cancellationToken).ConfigureAwait(false);
        if (result?.Id == null)
        {
            throw new ProvisioningFailedException(
                "Docker API returned null secret ID",
                secretName);
        }

        _logger.LogInformation("Created Docker secret {SecretId} ({SecretName})", result.Id, secretName);
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

        var response = await _httpClient.DeleteAsync($"/secrets/{secretId}", cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Removed Docker secret {SecretId}", secretId);
    }

    /// <summary>
    /// Builds the Secrets array for a Swarm service ContainerSpec.
    /// Always includes the config secret; optionally includes the KEK secret.
    /// </summary>
    private static object[] BuildSecretMounts(string configSecretId, string configSecretName, string? kekSecretId, string kekSecretName)
    {
        var configMount = new
        {
            SecretID = configSecretId,
            SecretName = configSecretName,
            File = new
            {
                Name = "xcord-config",
                UID = "0",
                GID = "0",
                Mode = 292u // 0444 octal
            }
        };

        if (string.IsNullOrWhiteSpace(kekSecretId))
        {
            return [configMount];
        }

        var kekMount = new
        {
            SecretID = kekSecretId,
            SecretName = kekSecretName,
            File = new
            {
                Name = "xcord-kek",
                UID = "1001", // xcord user inside the container
                GID = "1001",
                Mode = 256u // 0400 octal - owner-read only
            }
        };

        return [configMount, kekMount];
    }

    /// <summary>
    /// Looks up a Docker secret ID by name using the filter API.
    /// Returns null if no secret with the given name exists.
    /// </summary>
    private async Task<string?> GetSecretIdByNameAsync(string secretName, CancellationToken cancellationToken)
    {
        var filter = Uri.EscapeDataString($"{{\"name\":[\"{secretName}\"]}}");
        var response = await _httpClient.GetAsync($"/secrets?filters={filter}", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var secrets = await response.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken).ConfigureAwait(false);
        if (secrets == null || secrets.Length == 0) return null;
        return secrets[0].GetProperty("ID").GetString();
    }
}
