using Amazon;
using Amazon.Route53;
using Amazon.Route53.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace XcordHub.Infrastructure.Services;

public sealed class Route53DnsProvider : IDnsProvider, IDisposable
{
    private readonly AmazonRoute53Client _route53Client;
    private readonly ILogger<Route53DnsProvider> _logger;
    private readonly Route53Options _options;

    public Route53DnsProvider(
        IOptions<Route53Options> options,
        ILogger<Route53DnsProvider> logger)
    {
        _logger = logger;
        _options = options.Value;

        var config = new AmazonRoute53Config();
        if (!string.IsNullOrEmpty(_options.Endpoint))
            config.ServiceURL = _options.Endpoint;

        _route53Client = new AmazonRoute53Client(
            _options.AccessKeyId,
            _options.SecretAccessKey,
            config);
    }

    public async Task CreateARecordAsync(string subdomain, string ipAddress, CancellationToken cancellationToken = default)
    {
        var recordName = $"{ValidationHelpers.ExtractSubdomain(subdomain)}.{_options.DomainName}.";

        _logger.LogInformation("Creating Route53 A record {RecordName} -> {IpAddress}", recordName, ipAddress);

        var request = new ChangeResourceRecordSetsRequest
        {
            HostedZoneId = _options.HostedZoneId,
            ChangeBatch = new ChangeBatch
            {
                Changes =
                [
                    new Change
                    {
                        Action = ChangeAction.UPSERT,
                        ResourceRecordSet = new ResourceRecordSet
                        {
                            Name = recordName,
                            Type = RRType.A,
                            TTL = 120,
                            ResourceRecords = [new ResourceRecord { Value = ipAddress }]
                        }
                    }
                ]
            }
        };

        await _route53Client.ChangeResourceRecordSetsAsync(request, cancellationToken);

        _logger.LogInformation("Created Route53 A record {RecordName}", recordName);
    }

    public async Task<bool> VerifyDnsRecordAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        try
        {
            var recordName = $"{ValidationHelpers.ExtractSubdomain(subdomain)}.{_options.DomainName}.";

            var response = await _route53Client.ListResourceRecordSetsAsync(new ListResourceRecordSetsRequest
            {
                HostedZoneId = _options.HostedZoneId,
                StartRecordName = recordName,
                StartRecordType = RRType.A,
                MaxItems = "1"
            }, cancellationToken);

            return response.ResourceRecordSets.Any(r =>
                r.Name.Equals(recordName, StringComparison.OrdinalIgnoreCase) &&
                r.Type == RRType.A);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify Route53 DNS record {Subdomain}", subdomain);
            return false;
        }
    }

    public async Task DeleteARecordAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        var recordName = $"{ValidationHelpers.ExtractSubdomain(subdomain)}.{_options.DomainName}.";

        _logger.LogInformation("Deleting Route53 A record {RecordName}", recordName);

        // First find the record to get its current value
        var listResponse = await _route53Client.ListResourceRecordSetsAsync(new ListResourceRecordSetsRequest
        {
            HostedZoneId = _options.HostedZoneId,
            StartRecordName = recordName,
            StartRecordType = RRType.A,
            MaxItems = "1"
        }, cancellationToken);

        var existingRecord = listResponse.ResourceRecordSets.FirstOrDefault(r =>
            r.Name.Equals(recordName, StringComparison.OrdinalIgnoreCase) &&
            r.Type == RRType.A);

        if (existingRecord == null)
        {
            _logger.LogWarning("Route53 DNS record {RecordName} not found", recordName);
            return;
        }

        var request = new ChangeResourceRecordSetsRequest
        {
            HostedZoneId = _options.HostedZoneId,
            ChangeBatch = new ChangeBatch
            {
                Changes =
                [
                    new Change
                    {
                        Action = ChangeAction.DELETE,
                        ResourceRecordSet = existingRecord
                    }
                ]
            }
        };

        await _route53Client.ChangeResourceRecordSetsAsync(request, cancellationToken);

        _logger.LogInformation("Deleted Route53 A record {RecordName}", recordName);
    }

    public void Dispose()
    {
        _route53Client.Dispose();
    }
}

public sealed class Route53Options
{
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string HostedZoneId { get; set; } = string.Empty;
    public string DomainName { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
}
