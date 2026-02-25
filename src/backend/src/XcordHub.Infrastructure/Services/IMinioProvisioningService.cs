namespace XcordHub.Infrastructure.Services;

public interface IMinioProvisioningService
{
    /// <summary>
    /// Creates a MinIO user, bucket, and bucket policy restricted to that user.
    /// Idempotent — safe to call if the bucket or user already exist.
    /// </summary>
    Task ProvisionBucketAsync(string bucketName, string accessKey, string secretKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the MinIO user, its associated policy, empties, and removes the bucket.
    /// Safe to call even if resources are partially missing.
    /// </summary>
    Task DeprovisionBucketAsync(string bucketName, string accessKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the bucket exists and that the supplied per-instance credentials can access it.
    /// </summary>
    Task<bool> VerifyBucketAsync(string bucketName, string accessKey, string secretKey, CancellationToken cancellationToken = default);
}
