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

public sealed record SuspendInstanceCommand(long InstanceId, long UserId);

public sealed class SuspendInstanceHandler(
    HubDbContext dbContext,
    IDockerService dockerService,
    IInstanceNotifier instanceNotifier,
    ILogger<SuspendInstanceHandler> logger)
    : IRequestHandler<SuspendInstanceCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SuspendInstanceCommand request, CancellationToken cancellationToken)
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
            return Error.Forbidden("NOT_OWNER", "You do not have permission to suspend this instance");
        }

        // Can only suspend Running instances
        if (instance.Status != InstanceStatus.Running)
        {
            return Error.BadRequest("INVALID_STATUS", $"Cannot suspend instance in {instance.Status} status");
        }

        if (instance.Infrastructure == null)
        {
            return Error.NotFound("INFRASTRUCTURE_NOT_FOUND", "Instance infrastructure not found");
        }

        try
        {
            logger.LogInformation(
                "Suspending instance {InstanceId} ({Domain})",
                instance.Id, instance.Domain);

            // Notify the instance so it can relay System_ShuttingDown to connected clients.
            // The notifier absorbs all errors — if the instance is already unreachable we
            // still proceed with the suspension.
            await instanceNotifier.NotifyShuttingDownAsync(
                instance.Domain,
                "suspended by hub",
                cancellationToken);

            // Grace period: give the instance time to broadcast the notice to clients.
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

            // Stop the container
            await dockerService.StopContainerAsync(
                instance.Infrastructure.DockerContainerId,
                cancellationToken);

            // Update status
            instance.Status = InstanceStatus.Suspended;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Instance {InstanceId} ({Domain}) suspended successfully",
                instance.Id, instance.Domain);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to suspend instance {InstanceId} ({Domain}): {Error}",
                instance.Id, instance.Domain, ex.Message);

            return Error.Failure("SUSPEND_FAILED", $"Failed to suspend instance: {ex.Message}");
        }
    }

}

public static class SuspendInstanceEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/admin/instances/{instanceId:long}/suspend", async (
            [FromRoute] long instanceId,
            ClaimsPrincipal user,
            SuspendInstanceHandler handler,
            CancellationToken ct) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !long.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            var command = new SuspendInstanceCommand(instanceId, userId);
            var result = await handler.Handle(command, ct);

            return result.Match(
                success => Results.Ok(new SuccessResponse(true)),
                error => Results.Json(new { Error = error.Code, Message = error.Message }, statusCode: error.StatusCode));
        })
        .RequireAuthorization(Policies.User)
        .Produces<SuccessResponse>(200)
        .WithTags("Instances");
    }
}
