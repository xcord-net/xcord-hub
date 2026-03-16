using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Instances;

public sealed record UpdateBatchUpgradesRequest(bool Enabled);

public sealed record UpdateBatchUpgradesCommand(long InstanceId, long UserId, bool Enabled);

public sealed record UpdateBatchUpgradesResponse(bool BatchUpgradesEnabled);

public sealed class UpdateBatchUpgradesHandler(HubDbContext dbContext)
    : IRequestHandler<UpdateBatchUpgradesCommand, Result<UpdateBatchUpgradesResponse>>
{
    public async Task<Result<UpdateBatchUpgradesResponse>> Handle(
        UpdateBatchUpgradesCommand request, CancellationToken cancellationToken)
    {
        var instance = await dbContext.ManagedInstances
            .Include(i => i.Config)
            .FirstOrDefaultAsync(i => i.Id == request.InstanceId && i.DeletedAt == null, cancellationToken);

        if (instance == null)
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");

        if (instance.OwnerId != request.UserId)
            return Error.Forbidden("NOT_OWNER", "You do not have permission to manage this instance");

        if (instance.Config == null)
            return Error.NotFound("CONFIG_NOT_FOUND", "Instance configuration not found");

        instance.Config.BatchUpgradesEnabled = request.Enabled;
        instance.Config.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpdateBatchUpgradesResponse(instance.Config.BatchUpgradesEnabled);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPatch("/api/v1/hub/instances/{instanceId:long}/batch-upgrades", async (
            [FromRoute] long instanceId,
            UpdateBatchUpgradesRequest request,
            ClaimsPrincipal user,
            UpdateBatchUpgradesHandler handler,
            CancellationToken ct) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !long.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            var command = new UpdateBatchUpgradesCommand(instanceId, userId, request.Enabled);
            return await handler.ExecuteAsync(command, ct);
        })
        .RequireAuthorization(Policies.User)
        .Produces<UpdateBatchUpgradesResponse>(200)
        .WithName("UpdateBatchUpgrades")
        .WithTags("Instances", "Upgrades");
    }
}
