using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;
using XcordHub;

namespace XcordHub.Features.Provisioning;

/// <summary>
/// Creates a per-instance MinIO bucket and (optionally) a dedicated IAM user.
/// When per-user provisioning fails, falls back to root credentials so the instance
/// can still access its bucket.
/// </summary>
public sealed class ProvisionMinioStep : IProvisioningStep
{
    private readonly HubDbContext _dbContext;
    private readonly IMinioProvisioningService _minioService;
    private readonly MinioOptions _minioOptions;
    private readonly ILogger<ProvisionMinioStep> _logger;

    public string StepName => "ProvisionMinio";

    public ProvisionMinioStep(
        HubDbContext dbContext,
        IMinioProvisioningService minioService,
        IOptions<MinioOptions> minioOptions,
        ILogger<ProvisionMinioStep> logger)
    {
        _dbContext = dbContext;
        _minioService = minioService;
        _minioOptions = minioOptions.Value;
        _logger = logger;
    }

    public async Task<Result<bool>> ExecuteAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.ManagedInstances
            .Include(i => i.Infrastructure)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance?.Infrastructure == null)
        {
            return Error.NotFound("INFRASTRUCTURE_NOT_FOUND", $"Infrastructure for instance {instanceId} not found");
        }

        var infra = instance.Infrastructure;
        var subdomain = instance.Domain.Split('.')[0];
        var bucketName = $"xcord-{subdomain}";

        _logger.LogInformation(
            "Provisioning MinIO bucket {Bucket} for instance {InstanceId} ({Domain})",
            bucketName, instanceId, instance.Domain);

        try
        {
            await _minioService.ProvisionBucketAsync(
                bucketName,
                infra.MinioAccessKey,
                infra.MinioSecretKey,
                cancellationToken);

            // Check if the per-instance credentials actually work
            var perUserWorks = await _minioService.VerifyBucketAsync(
                bucketName, infra.MinioAccessKey, infra.MinioSecretKey, cancellationToken);

            if (!perUserWorks)
            {
                // Per-instance IAM user creation likely failed (Console API unavailable).
                // Fall back to root credentials so the instance can still access its bucket.
                _logger.LogWarning(
                    "Per-instance MinIO credentials failed for instance {InstanceId}. " +
                    "Falling back to root credentials for bucket {Bucket}.",
                    instanceId, bucketName);

                infra.MinioAccessKey = _minioOptions.AccessKey;
                infra.MinioSecretKey = _minioOptions.SecretKey;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MinIO provisioning failed for instance {InstanceId} ({Domain}): {Error}",
                instanceId, instance.Domain, ex.Message);

            return Error.Failure("MINIO_PROVISION_FAILED", $"Failed to provision MinIO bucket: {ex.Message}");
        }
    }

    public async Task<Result<bool>> VerifyAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.ManagedInstances
            .Include(i => i.Infrastructure)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance?.Infrastructure == null)
        {
            return Error.NotFound("INFRASTRUCTURE_NOT_FOUND", $"Infrastructure for instance {instanceId} not found");
        }

        var infra = instance.Infrastructure;
        var subdomain = instance.Domain.Split('.')[0];
        var bucketName = $"xcord-{subdomain}";

        try
        {
            var verified = await _minioService.VerifyBucketAsync(
                bucketName,
                infra.MinioAccessKey,
                infra.MinioSecretKey,
                cancellationToken);

            return verified
                ? true
                : Error.Failure("MINIO_VERIFY_FAILED", $"MinIO bucket '{bucketName}' verification failed");
        }
        catch (Exception ex)
        {
            return Error.Failure("MINIO_VERIFY_ERROR", $"MinIO bucket verification error: {ex.Message}");
        }
    }
}
