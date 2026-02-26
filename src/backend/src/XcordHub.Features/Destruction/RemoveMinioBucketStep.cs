using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Destruction;

public sealed class RemoveMinioBucketStep(IMinioProvisioningService minioService, ILogger<RemoveMinioBucketStep> logger) : IDestructionStep
{
    public string StepName => "RemoveMinioBucket";

    public async Task ExecuteAsync(ManagedInstance instance, InstanceInfrastructure infrastructure, CancellationToken cancellationToken)
    {
        var subdomain = ValidationHelpers.ExtractSubdomain(instance.Domain);
        var bucketName = $"xcord-{subdomain}";
        logger.LogInformation("Removing MinIO bucket {Bucket} for {Domain}", bucketName, instance.Domain);
        await minioService.DeprovisionBucketAsync(bucketName, infrastructure.MinioAccessKey, cancellationToken);
    }
}
