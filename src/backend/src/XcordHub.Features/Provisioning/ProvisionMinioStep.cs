using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using XcordHub;

namespace XcordHub.Features.Provisioning;

/// <summary>
/// Creates a per-instance MinIO bucket with a dedicated IAM user.
/// Provisioning fails hard if per-instance credentials cannot be verified —
/// never falls back to root credentials (which would give cross-bucket access).
/// </summary>
public sealed class ProvisionMinioStep : IProvisioningStep
{
    private readonly HubDbContext _dbContext;
    private readonly IMinioProvisioningService _minioService;
    private readonly ILogger<ProvisionMinioStep> _logger;

    public string StepName => "ProvisionMinio";

    public ProvisionMinioStep(
        HubDbContext dbContext,
        IMinioProvisioningService minioService,
        ILogger<ProvisionMinioStep> logger)
    {
        _dbContext = dbContext;
        _minioService = minioService;
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
        var subdomain = ValidationHelpers.ExtractSubdomain(instance.Domain);
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

            // Verify per-instance credentials work — never fall back to root credentials
            var perUserWorks = await _minioService.VerifyBucketAsync(
                bucketName, infra.MinioAccessKey, infra.MinioSecretKey, cancellationToken);

            if (!perUserWorks)
            {
                return Error.Failure("MINIO_CREDENTIALS_FAILED",
                    $"Per-instance MinIO credentials failed for bucket '{bucketName}'. " +
                    "Ensure the MinIO Console API is available for IAM user provisioning.");
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
        var subdomain = ValidationHelpers.ExtractSubdomain(instance.Domain);
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
