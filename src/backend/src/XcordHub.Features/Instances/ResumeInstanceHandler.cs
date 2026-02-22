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

public sealed record ResumeInstanceCommand(long InstanceId, long UserId);

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
        if (instance.OwnerId != request.UserId)
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

            // Container should be in stopped state, Docker restart policy will handle restart
            // For now, verify the container is running (restart policy would have started it)
            var isRunning = await dockerService.VerifyContainerRunningAsync(
                instance.Infrastructure.DockerContainerId,
                cancellationToken);

            if (!isRunning)
            {
                logger.LogWarning(
                    "Instance {InstanceId} container not running after resume attempt, may need manual intervention",
                    instance.Id);
            }

            // Update status
            instance.Status = InstanceStatus.Running;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Instance {InstanceId} ({Domain}) resumed successfully",
                instance.Id, instance.Domain);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to resume instance {InstanceId} ({Domain}): {Error}",
                instance.Id, instance.Domain, ex.Message);

            return Error.Failure("RESUME_FAILED", $"Failed to resume instance: {ex.Message}");
        }
    }

}

public static class ResumeInstanceEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/admin/instances/{instanceId:long}/resume", async (
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
            var result = await handler.Handle(command, ct);

            return result.Match(
                success => Results.Ok(new { Success = true }),
                error => Results.Json(new { Error = error.Code, Message = error.Message }, statusCode: error.StatusCode));
        })
        .RequireAuthorization(Policies.User)
        .WithTags("Instances");
    }
}
