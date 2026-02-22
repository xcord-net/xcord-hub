namespace XcordHub.Infrastructure.Services;

public interface IStorageManager
{
    Task DeleteBucketAsync(string bucketName, CancellationToken cancellationToken = default);
    Task<bool> VerifyBucketExistsAsync(string bucketName, CancellationToken cancellationToken = default);
}
