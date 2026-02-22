using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace XcordHub.Infrastructure.Services;

public sealed class CloudflareDnsProvider : IDnsProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CloudflareDnsProvider> _logger;
    private readonly CloudflareOptions _options;

    public CloudflareDnsProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<CloudflareOptions> options,
        ILogger<CloudflareDnsProvider> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Cloudflare");
        _logger = logger;
        _options = options.Value;
    }

    public async Task CreateARecordAsync(string subdomain, string ipAddress, CancellationToken cancellationToken = default)
    {
        // Extract subdomain from full domain (e.g., "myserver.xcord.net" -> "myserver")
        var recordName = subdomain.Contains('.') ? subdomain.Split('.')[0] : subdomain;

        var payload = new
        {
            type = "A",
            name = recordName,
            content = ipAddress,
            ttl = 120,
            proxied = false
        };

        _logger.LogInformation("Creating Cloudflare A record {RecordName} -> {IpAddress}", recordName, ipAddress);

        var response = await _httpClient.PostAsJsonAsync(
            $"/client/v4/zones/{_options.ZoneId}/dns_records",
            payload,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to create Cloudflare DNS record: {error}");
        }

        _logger.LogInformation("Created Cloudflare A record {RecordName}", recordName);
    }

    public async Task<bool> VerifyDnsRecordAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        try
        {
            var recordName = subdomain.Contains('.') ? subdomain.Split('.')[0] : subdomain;

            var response = await _httpClient.GetAsync(
                $"/client/v4/zones/{_options.ZoneId}/dns_records?type=A&name={recordName}.{_options.DomainName}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<CloudflareListResponse>(cancellationToken);
            return result?.Result?.Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify Cloudflare DNS record {Subdomain}", subdomain);
            return false;
        }
    }

    public async Task DeleteARecordAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        var recordName = subdomain.Contains('.') ? subdomain.Split('.')[0] : subdomain;

        _logger.LogInformation("Deleting Cloudflare A record {RecordName}", recordName);

        // First, find the record ID
        var listResponse = await _httpClient.GetAsync(
            $"/client/v4/zones/{_options.ZoneId}/dns_records?type=A&name={recordName}.{_options.DomainName}",
            cancellationToken);

        if (!listResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to find Cloudflare DNS record {RecordName}", recordName);
            return;
        }

        var listResult = await listResponse.Content.ReadFromJsonAsync<CloudflareListResponse>(cancellationToken);
        if (listResult?.Result == null || listResult.Result.Length == 0)
        {
            _logger.LogWarning("Cloudflare DNS record {RecordName} not found", recordName);
            return;
        }

        var recordId = listResult.Result[0].Id;

        // Delete the record
        var deleteResponse = await _httpClient.DeleteAsync(
            $"/client/v4/zones/{_options.ZoneId}/dns_records/{recordId}",
            cancellationToken);

        if (!deleteResponse.IsSuccessStatusCode)
        {
            var error = await deleteResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to delete Cloudflare DNS record: {error}");
        }

        _logger.LogInformation("Deleted Cloudflare A record {RecordName}", recordName);
    }

    private sealed class CloudflareListResponse
    {
        public CloudflareDnsRecord[]? Result { get; set; }
    }

    private sealed class CloudflareDnsRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}

public sealed class CloudflareOptions
{
    public string ApiToken { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public string DomainName { get; set; } = string.Empty;
}
