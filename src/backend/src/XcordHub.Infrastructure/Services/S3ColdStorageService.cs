using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XcordHub.Infrastructure.Options;

namespace XcordHub.Infrastructure.Services;

public sealed class S3ColdStorageService : IColdStorageService
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly ILogger<S3ColdStorageService> _logger;

    public S3ColdStorageService(IOptions<ColdStorageOptions> options, ILogger<S3ColdStorageService> logger)
    {
        _logger = logger;
        var opts = options.Value;
        _bucket = opts.Bucket;

        var config = new AmazonS3Config();
        if (!string.IsNullOrEmpty(opts.Endpoint))
        {
            config.ServiceURL = opts.Endpoint.StartsWith("http") ? opts.Endpoint : $"https://{opts.Endpoint}";
            config.ForcePathStyle = true;
        }
        if (!string.IsNullOrEmpty(opts.Region))
            config.AuthenticationRegion = opts.Region;

        _client = new AmazonS3Client(opts.AccessKey, opts.SecretKey, config);
    }

    public async Task UploadAsync(string key, Stream content, CancellationToken ct = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content
        };
        await _client.PutObjectAsync(request, ct);
        _logger.LogInformation("Uploaded backup to {Key} ({Bucket})", key, _bucket);
    }

    public async Task<Stream> DownloadAsync(string key, CancellationToken ct = default)
    {
        var response = await _client.GetObjectAsync(_bucket, key, ct);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await _client.DeleteObjectAsync(_bucket, key, ct);
        _logger.LogInformation("Deleted backup {Key} from {Bucket}", key, _bucket);
    }

    public async Task<long> GetObjectSizeAsync(string key, CancellationToken ct = default)
    {
        var metadata = await _client.GetObjectMetadataAsync(_bucket, key, ct);
        return metadata.ContentLength;
    }

    public async Task<IReadOnlyList<string>> ListObjectsAsync(string prefix, CancellationToken ct = default)
    {
        var request = new ListObjectsV2Request
        {
            BucketName = _bucket,
            Prefix = prefix
        };
        var keys = new List<string>();
        ListObjectsV2Response response;
        do
        {
            response = await _client.ListObjectsV2Async(request, ct);
            keys.AddRange(response.S3Objects.Select(o => o.Key));
            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated);

        return keys;
    }
}
