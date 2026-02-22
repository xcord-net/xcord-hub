using Microsoft.Extensions.Logging;

namespace XcordHub.Infrastructure.Services;

public sealed class NoopStorageManager : IStorageManager
{
    private readonly ILogger<NoopStorageManager> _logger;

    public NoopStorageManager(ILogger<NoopStorageManager> logger)
    {
        _logger = logger;
    }

    public Task DeleteBucketAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NOOP: Would delete bucket {BucketName}", bucketName);
        return Task.CompletedTask;
    }

    public Task<bool> VerifyBucketExistsAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NOOP: Would verify bucket {BucketName} exists", bucketName);
        return Task.FromResult(true);
    }
}
