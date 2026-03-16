using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Features.Upgrades;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Instances;

public sealed record UpgradeInstanceRequest(string TargetImage);

public sealed record UpgradeInstanceCommand(long InstanceId, long UserId, string TargetImage);

public sealed record UpgradeInstanceResponse(bool Accepted);

public sealed class UpgradeInstanceHandler(HubDbContext dbContext, IUpgradeQueue upgradeQueue)
    : IRequestHandler<UpgradeInstanceCommand, Result<UpgradeInstanceResponse>>
{
    public async Task<Result<UpgradeInstanceResponse>> Handle(
        UpgradeInstanceCommand request, CancellationToken cancellationToken)
    {
        var instance = await dbContext.ManagedInstances
            .Include(i => i.Config)
            .FirstOrDefaultAsync(i => i.Id == request.InstanceId && i.DeletedAt == null, cancellationToken);

        if (instance == null)
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");

        if (instance.OwnerId != request.UserId)
            return Error.Forbidden("NOT_OWNER", "You do not have permission to manage this instance");

        if (instance.Status != InstanceStatus.Running)
            return Error.BadRequest("INVALID_STATUS", $"Cannot upgrade instance in {instance.Status} status");

        await upgradeQueue.EnqueueInstanceUpgradeAsync(
            request.InstanceId, request.TargetImage, cancellationToken: cancellationToken);

        return new UpgradeInstanceResponse(true);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/hub/instances/{instanceId:long}/upgrade", async (
            [FromRoute] long instanceId,
            UpgradeInstanceRequest request,
            ClaimsPrincipal user,
            UpgradeInstanceHandler handler,
            CancellationToken ct) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !long.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            var command = new UpgradeInstanceCommand(instanceId, userId, request.TargetImage);
            return await handler.ExecuteAsync(command, ct,
                success => Results.Accepted(null, success));
        })
        .RequireAuthorization(Policies.User)
        .Produces<UpgradeInstanceResponse>(202)
        .WithName("UpgradeInstance")
        .WithTags("Instances", "Upgrades");
    }
}
