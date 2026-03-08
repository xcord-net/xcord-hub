using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Instances;

public sealed record UpdateUpgradePolicyRequest(UpgradePolicy UpgradePolicy, string? PinnedVersion);

public sealed record UpdateUpgradePolicyCommand(long InstanceId, long UserId, UpgradePolicy UpgradePolicy, string? PinnedVersion);

public sealed record UpdateUpgradePolicyResponse(string UpgradePolicy, string? PinnedVersion);

public sealed class UpdateUpgradePolicyHandler(HubDbContext dbContext)
    : IRequestHandler<UpdateUpgradePolicyCommand, Result<UpdateUpgradePolicyResponse>>
{
    public async Task<Result<UpdateUpgradePolicyResponse>> Handle(
        UpdateUpgradePolicyCommand request, CancellationToken cancellationToken)
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

        if (!Enum.IsDefined(request.UpgradePolicy))
            return Error.BadRequest("INVALID_POLICY", "Invalid upgrade policy value");

        if (request.UpgradePolicy == UpgradePolicy.Pinned && string.IsNullOrWhiteSpace(request.PinnedVersion))
            return Error.BadRequest("PINNED_VERSION_REQUIRED", "A pinned version is required when upgrade policy is Pinned");

        instance.Config.UpgradePolicy = request.UpgradePolicy;
        instance.Config.PinnedVersion = request.UpgradePolicy == UpgradePolicy.Pinned
            ? request.PinnedVersion
            : null;
        instance.Config.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpdateUpgradePolicyResponse(
            instance.Config.UpgradePolicy.ToString(),
            instance.Config.PinnedVersion);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPatch("/api/v1/hub/instances/{instanceId:long}/upgrade-policy", async (
            [FromRoute] long instanceId,
            UpdateUpgradePolicyRequest request,
            ClaimsPrincipal user,
            UpdateUpgradePolicyHandler handler,
            CancellationToken ct) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !long.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            var command = new UpdateUpgradePolicyCommand(
                instanceId, userId, request.UpgradePolicy, request.PinnedVersion);
            return await handler.ExecuteAsync(command, ct);
        })
        .RequireAuthorization(Policies.User)
        .Produces<UpdateUpgradePolicyResponse>(200)
        .WithName("UpdateUpgradePolicy")
        .WithTags("Instances", "Upgrades");
    }
}
