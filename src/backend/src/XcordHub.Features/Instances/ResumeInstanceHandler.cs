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

/// <param name="AsPlatformAdmin">
///   Set by the admin-scoped route. A platform operator acts on any instance in
///   the fleet, not only the ones they happen to own.
/// </param>
public sealed record ResumeInstanceCommand(long InstanceId, long UserId, bool AsPlatformAdmin = false);

public sealed class ResumeInstanceHandler(
    HubDbContext dbContext,
    IDockerService dockerService,
    ILogger<ResumeInstanceHandler> logger)
    : IRequestHandler<ResumeInstanceCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ResumeInstanceCommand request, CancellationToken cancellationToken)
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
            return Error.Forbidden("NOT_OWNER", "You do not have permission to resume this instance");
        }

        // Can only resume Suspended instances
        if (instance.Status != InstanceStatus.Suspended)
        {
            return Error.BadRequest("INVALID_STATUS", $"Cannot resume instance in {instance.Status} status");
        }

        if (instance.Infrastructure == null)
        {
            return Error.NotFound("INFRASTRUCTURE_NOT_FOUND", "Instance infrastructure not found");
        }

        try
        {
            logger.LogInformation(
                "Resuming instance {InstanceId} ({Domain})",
                instance.Id, instance.Domain);

            // Suspend scales the service to zero replicas, so resume has to scale
            // it back. Previously this only *checked* whether the container was
            // running and, when it was not, logged a warning and reported success
            // anyway - leaving the instance marked Running with nothing serving
            // it, and no way back short of manual intervention.
            await dockerService.StartExistingContainerAsync(
                instance.Infrastructure.DockerContainerId,
                cancellationToken).ConfigureAwait(false);

            var isRunning = await dockerService.VerifyContainerRunningAsync(
                instance.Infrastructure.DockerContainerId,
                cancellationToken);

            if (!isRunning)
            {
                logger.LogError(
                    "Instance {InstanceId} container did not come back after resume",
                    instance.Id);

                return Error.Failure("RESUME_FAILED",
                    "The instance container did not start. It remains suspended.");
            }

            // Update status - optimistic concurrency via xmin ensures only one concurrent
            // resume wins; the other gets DbUpdateConcurrencyException → 409 Conflict.
            instance.Status = InstanceStatus.Running;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Instance {InstanceId} ({Domain}) resumed successfully",
                instance.Id, instance.Domain);

            return true;
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex,
                "Concurrent resume conflict for instance {InstanceId} ({Domain})",
                instance.Id, instance.Domain);

            return Error.Conflict("CONCURRENT_MODIFICATION",
                "Instance was modified concurrently. Please retry the operation.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to resume instance {InstanceId} ({Domain}): {Error}",
                instance.Id, instance.Domain, ex.Message);

            return Error.Failure("RESUME_FAILED", $"Failed to resume instance: {ex.Message}");
        }
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        // Platform-operator route. The admin console has always called this
        // path; until now it did not exist, so every lifecycle action in the
        // console failed with 405. Ownership is bypassed deliberately - an
        // operator acts on the whole fleet, and Policies.Admin is the gate.
        app.MapPost("/api/v1/admin/instances/{instanceId:long}/resume", async (
            [FromRoute] long instanceId,
            ClaimsPrincipal user,
            ResumeInstanceHandler handler,
            CancellationToken ct) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !long.TryParse(userIdClaim, out var adminId))
            {
                return Results.Unauthorized();
            }

            var adminCommand = new ResumeInstanceCommand(instanceId, adminId, AsPlatformAdmin: true);
            var adminResult = await handler.Handle(adminCommand, ct).ConfigureAwait(false);

            return adminResult.Match(
                success => Results.Ok(new SuccessResponse(true)),
                error => Results.Json(new { Error = error.Code, Message = error.Message }, statusCode: error.StatusCode));
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<SuccessResponse>(200)
        .WithName("AdminResumeInstance")
        .WithTags("Admin");

        return app.MapPost("/api/v1/hub/instances/{instanceId:long}/resume", async (
            [FromRoute] long instanceId,
            ClaimsPrincipal user,
            ResumeInstanceHandler handler,
            CancellationToken ct) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !long.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            var command = new ResumeInstanceCommand(instanceId, userId);
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
