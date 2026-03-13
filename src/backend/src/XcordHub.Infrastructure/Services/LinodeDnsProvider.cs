using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace XcordHub.Infrastructure.Services;

public sealed class LinodeDnsProvider : IDnsProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LinodeDnsProvider> _logger;
    private readonly LinodeOptions _options;

    public LinodeDnsProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<LinodeOptions> options,
        ILogger<LinodeDnsProvider> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Linode");
        _logger = logger;
        _options = options.Value;
    }

    public async Task CreateARecordAsync(string subdomain, string ipAddress, CancellationToken cancellationToken = default)
    {
        var recordName = ValidationHelpers.ExtractSubdomain(subdomain);

        var payload = new
        {
            type = "A",
            name = recordName,
            target = ipAddress,
            ttl_sec = 120
        };

        _logger.LogInformation("Creating Linode A record {RecordName} -> {IpAddress}", recordName, ipAddress);

        var response = await _httpClient.PostAsJsonAsync(
            $"/domains/{_options.DomainId}/records",
            payload,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to create Linode DNS record: {error}");
        }

        _logger.LogInformation("Created Linode A record {RecordName}", recordName);
    }

    public async Task<bool> VerifyDnsRecordAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        try
        {
            var recordName = ValidationHelpers.ExtractSubdomain(subdomain);

            var response = await _httpClient.GetAsync(
                $"/domains/{_options.DomainId}/records?type=A",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<LinodeListResponse>(cancellationToken);
            return result?.Data?.Any(r => r.Name.Equals(recordName, StringComparison.OrdinalIgnoreCase)) ?? false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify Linode DNS record {Subdomain}", subdomain);
            return false;
        }
    }

    public async Task DeleteARecordAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        var recordName = ValidationHelpers.ExtractSubdomain(subdomain);

        _logger.LogInformation("Deleting Linode A record {RecordName}", recordName);

        // First, find the record ID
        var listResponse = await _httpClient.GetAsync(
            $"/domains/{_options.DomainId}/records?type=A",
            cancellationToken);

        if (!listResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to list Linode DNS records for {RecordName}", recordName);
            return;
        }

        var listResult = await listResponse.Content.ReadFromJsonAsync<LinodeListResponse>(cancellationToken);
        var record = listResult?.Data?.FirstOrDefault(r => r.Name.Equals(recordName, StringComparison.OrdinalIgnoreCase));

        if (record == null)
        {
            _logger.LogWarning("Linode DNS record {RecordName} not found", recordName);
            return;
        }

        // Delete the record
        var deleteResponse = await _httpClient.DeleteAsync(
            $"/domains/{_options.DomainId}/records/{record.Id}",
            cancellationToken);

        if (!deleteResponse.IsSuccessStatusCode)
        {
            var error = await deleteResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to delete Linode DNS record: {error}");
        }

        _logger.LogInformation("Deleted Linode A record {RecordName}", recordName);
    }

    private sealed class LinodeListResponse
    {
        public LinodeDnsRecord[]? Data { get; set; }
    }

    private sealed class LinodeDnsRecord
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
    }
}

public sealed class LinodeOptions
{
    public string ApiToken { get; set; } = string.Empty;
    public int DomainId { get; set; }
}
