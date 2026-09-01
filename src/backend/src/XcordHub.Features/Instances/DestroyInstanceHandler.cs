using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Features.Destruction;
using XcordHub.Infrastructure.Data;
using XcordHub.Shared.Extensions;

namespace XcordHub.Features.Instances;

/// <param name="AsPlatformAdmin">
///   Set by the admin-scoped route. A platform operator acts on any instance in
///   the fleet, not only the ones they happen to own.
/// </param>
public sealed record DestroyInstanceCommand(long InstanceId, long UserId, bool AsPlatformAdmin = false);

public sealed class DestroyInstanceHandler(
    HubDbContext dbContext,
    DestructionPipeline destructionPipeline,
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
        if (!request.AsPlatformAdmin && instance.OwnerId != request.UserId)
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
            if (instance.Infrastructure != null)
            {
                await destructionPipeline.RunAsync(instance, instance.Infrastructure, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                logger.LogWarning("Instance {InstanceId} has no infrastructure to clean up", instance.Id);
            }

            // Tombstone the worker ID (never reuse)
            await TombstoneWorkerIdAsync(instance.SnowflakeWorkerId, cancellationToken).ConfigureAwait(false);

            // Mark instance as destroyed (soft delete) - optimistic concurrency via xmin ensures
            // only one concurrent destroy wins; the other gets DbUpdateConcurrencyException → 409.
            instance.Status = InstanceStatus.Destroyed;
            instance.SoftDelete();
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Instance {InstanceId} ({Domain}) destroyed successfully",
                instance.Id, instance.Domain);

            return true;
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex,
                "Concurrent destruction conflict for instance {InstanceId} ({Domain})",
                instance.Id, instance.Domain);

            return Error.Conflict("CONCURRENT_MODIFICATION",
                "Instance was modified concurrently. Please retry the operation.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to destroy instance {InstanceId} ({Domain}): {Error}",
                instance.Id, instance.Domain, ex.Message);

            return Error.Failure("DESTROY_FAILED", $"Failed to destroy instance: {ex.Message}");
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
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Tombstoned worker ID {WorkerId}", workerId);
        }
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        // Platform-operator route. The admin console has always called this
        // path; until now it did not exist, so every lifecycle action in the
        // console failed with 405. Ownership is bypassed deliberately - an
        // operator acts on the whole fleet, and Policies.Admin is the gate.
        app.MapDelete("/api/v1/admin/instances/{instanceId:long}", async (
            [FromRoute] long instanceId,
            ClaimsPrincipal user,
            DestroyInstanceHandler handler,
            CancellationToken ct) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !long.TryParse(userIdClaim, out var adminId))
            {
                return Results.Unauthorized();
            }

            var adminCommand = new DestroyInstanceCommand(instanceId, adminId, AsPlatformAdmin: true);
            var adminResult = await handler.Handle(adminCommand, ct).ConfigureAwait(false);

            return adminResult.Match(
                success => Results.Ok(new SuccessResponse(true)),
                error => Results.Json(new { Error = error.Code, Message = error.Message }, statusCode: error.StatusCode));
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<SuccessResponse>(200)
        .WithName("AdminDestroyInstance")
        .WithTags("Admin");

        return app.MapPost("/api/v1/hub/instances/{instanceId:long}/destroy", async (
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
            var result = await handler.Handle(command, ct).ConfigureAwait(false);

            return result.Match(
                success => Results.Ok(new SuccessResponse(true)),
                error => Results.Json(new { Error = error.Code, Message = error.Message }, statusCode: error.StatusCode));
        })
        .RequireAuthorization(Policies.User)
        .Produces<SuccessResponse>(200)
        .WithTags("Instances");
    }
}
