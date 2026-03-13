using Microsoft.Extensions.Logging;

namespace XcordHub.Infrastructure.Services;

public sealed class NoopColdStorageService : IColdStorageService
{
    private readonly ILogger<NoopColdStorageService> _logger;

    public NoopColdStorageService(ILogger<NoopColdStorageService> logger)
    {
        _logger = logger;
    }

    public Task UploadAsync(string key, Stream content, CancellationToken ct = default)
    {
        _logger.LogWarning("Cold storage not configured — skipping upload of {Key}", key);
        return Task.CompletedTask;
    }

    public Task<Stream> DownloadAsync(string key, CancellationToken ct = default)
    {
        _logger.LogWarning("Cold storage not configured — cannot download {Key}", key);
        return Task.FromResult(Stream.Null);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        _logger.LogWarning("Cold storage not configured — skipping delete of {Key}", key);
        return Task.CompletedTask;
    }

    public Task<long> GetObjectSizeAsync(string key, CancellationToken ct = default)
    {
        _logger.LogWarning("Cold storage not configured — cannot get size for {Key}", key);
        return Task.FromResult(0L);
    }

    public Task<IReadOnlyList<string>> ListObjectsAsync(string prefix, CancellationToken ct = default)
    {
        _logger.LogWarning("Cold storage not configured — cannot list objects with prefix {Prefix}", prefix);
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
