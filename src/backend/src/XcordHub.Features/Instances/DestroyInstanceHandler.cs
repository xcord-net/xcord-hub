using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Instances;

public sealed record DestroyInstanceCommand(long InstanceId, long UserId);

public sealed class DestroyInstanceHandler(
    HubDbContext dbContext,
    IDockerService dockerService,
    ICaddyProxyManager proxyManager,
    IDnsProvider dnsProvider,
    IDatabaseManager databaseManager,
    IStorageManager storageManager,
    ILogger<DestroyInstanceHandler> logger)
    : IRequestHandler<DestroyInstanceCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DestroyInstanceCommand request, CancellationToken cancellationToken)
    {
        var instance = await dbContext.ManagedInstances
            .Include(i => i.Infrastructure)
            .FirstOrDefaultAsync(i => i.Id == request.InstanceId && i.DeletedAt == null, cancellationToken);

        if (instance == null)
        {
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");
        }

        // Verify ownership
        if (instance.OwnerId != request.UserId)
        {
            return Error.Forbidden("NOT_OWNER", "You do not have permission to destroy this instance");
        }

        // Cannot destroy already destroyed instances
        if (instance.Status == InstanceStatus.Destroyed)
        {
            return Error.BadRequest("ALREADY_DESTROYED", "Instance is already destroyed");
        }

        try
        {
            logger.LogInformation(
                "Destroying instance {InstanceId} ({Domain})",
                instance.Id, instance.Domain);

            // Cleanup resources in reverse order of provisioning
            await CleanupInstanceAsync(instance, cancellationToken);

            // Tombstone the worker ID (never reuse)
            await TombstoneWorkerIdAsync(instance.SnowflakeWorkerId, cancellationToken);

            // Mark instance as destroyed (soft delete)
            instance.Status = InstanceStatus.Destroyed;
            instance.DeletedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Instance {InstanceId} ({Domain}) destroyed successfully",
                instance.Id, instance.Domain);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to destroy instance {InstanceId} ({Domain}): {Error}",
                instance.Id, instance.Domain, ex.Message);

            return Error.Failure("DESTROY_FAILED", $"Failed to destroy instance: {ex.Message}");
        }
    }

    private async Task CleanupInstanceAsync(
        ManagedInstance instance,
        CancellationToken cancellationToken)
    {
        var infrastructure = instance.Infrastructure;
        if (infrastructure == null)
        {
            logger.LogWarning(
                "Instance {InstanceId} has no infrastructure to clean up",
                instance.Id);
            return;
        }

        // 1. Stop container
        try
        {
            if (!string.IsNullOrWhiteSpace(infrastructure.DockerContainerId))
            {
                logger.LogInformation("Stopping container {ContainerId}", infrastructure.DockerContainerId);
                await dockerService.StopContainerAsync(infrastructure.DockerContainerId, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to stop container, continuing cleanup");
        }

        // 2. Remove proxy route
        try
        {
            if (!string.IsNullOrWhiteSpace(infrastructure.CaddyRouteId))
            {
                logger.LogInformation("Removing proxy route {RouteId}", infrastructure.CaddyRouteId);
                await proxyManager.DeleteRouteAsync(infrastructure.CaddyRouteId, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove proxy route, continuing cleanup");
        }

        // 3. Remove DNS record
        try
        {
            logger.LogInformation("Removing DNS record for {Domain}", instance.Domain);
            await dnsProvider.DeleteARecordAsync(instance.Domain, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove DNS record, continuing cleanup");
        }

        // 4. Drop database
        try
        {
            if (!string.IsNullOrWhiteSpace(infrastructure.DatabaseName))
            {
                logger.LogInformation("Dropping database {DatabaseName}", infrastructure.DatabaseName);
                await databaseManager.DropDatabaseAsync(infrastructure.DatabaseName, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to drop database, continuing cleanup");
        }

        // 5. Delete MinIO bucket
        try
        {
            var bucketName = $"xcord-{instance.Domain.Replace(".", "-")}";
            logger.LogInformation("Deleting storage bucket {BucketName}", bucketName);
            await storageManager.DeleteBucketAsync(bucketName, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete storage bucket, continuing cleanup");
        }

        // 6. Remove container
        try
        {
            if (!string.IsNullOrWhiteSpace(infrastructure.DockerContainerId))
            {
                logger.LogInformation("Removing container {ContainerId}", infrastructure.DockerContainerId);
                await dockerService.RemoveContainerAsync(infrastructure.DockerContainerId, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove container, continuing cleanup");
        }

        // 7. Remove network
        try
        {
            logger.LogInformation("Removing network for {Domain}", instance.Domain);
            await dockerService.RemoveNetworkAsync(instance.Domain, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove network, continuing cleanup");
        }
    }

    private async Task TombstoneWorkerIdAsync(long workerId, CancellationToken cancellationToken)
    {
        var workerIdRecord = await dbContext.WorkerIdRegistry
            .FirstOrDefaultAsync(w => w.WorkerId == (int)workerId, cancellationToken);

        if (workerIdRecord != null)
        {
            workerIdRecord.IsTombstoned = true;
            workerIdRecord.ReleasedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Tombstoned worker ID {WorkerId}", workerId);
        }
    }

}

public static class DestroyInstanceEndpoint
{
    public static void MapDestroyInstanceEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/v1/instances/{instanceId:long}", async (
            [FromRoute] long instanceId,
            ClaimsPrincipal user,
            DestroyInstanceHandler handler,
            CancellationToken ct) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !long.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            var command = new DestroyInstanceCommand(instanceId, userId);
            var result = await handler.Handle(command, ct);

            return result.Match(
                success => Results.Ok(new { Success = true }),
                error => Results.Json(new { Error = error.Code, Message = error.Message }, statusCode: error.StatusCode));
        })
        .RequireAuthorization(Policies.User)
        .WithTags("Instances");
    }
}
