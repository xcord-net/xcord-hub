using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Features.Upgrades;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Federation;

public sealed record FederationUpgradeRequest(string TargetVersion);

public sealed record FederationUpgradeCommand(long InstanceId, string TargetVersion);

public sealed record FederationUpgradeResponse(bool Accepted, string TargetImage);

public sealed class RequestUpgradeHandler(HubDbContext dbContext, IUpgradeQueue upgradeQueue)
    : IRequestHandler<FederationUpgradeCommand, Result<FederationUpgradeResponse>>,
      IValidatable<FederationUpgradeCommand>
{
    public Error? Validate(FederationUpgradeCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.TargetVersion))
            return Error.Validation("VALIDATION_FAILED", "TargetVersion is required");
        return null;
    }

    public async Task<Result<FederationUpgradeResponse>> Handle(
        FederationUpgradeCommand request, CancellationToken cancellationToken)
    {
        var instance = await dbContext.ManagedInstances
            .FirstOrDefaultAsync(i => i.Id == request.InstanceId && i.DeletedAt == null, cancellationToken);

        if (instance == null)
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");

        if (instance.Status == InstanceStatus.Upgrading)
            return Error.Conflict("ALREADY_UPGRADING", "Instance is already being upgraded");

        if (instance.Status != InstanceStatus.Running && instance.Status != InstanceStatus.Failed)
            return Error.BadRequest("INVALID_STATUS", $"Cannot upgrade instance in {instance.Status} status");

        var version = await dbContext.AvailableVersions
            .FirstOrDefaultAsync(v => v.Version == request.TargetVersion && v.DeletedAt == null, cancellationToken);

        if (version == null)
            return Error.NotFound("VERSION_NOT_FOUND", $"Version '{request.TargetVersion}' not found");

        await upgradeQueue.EnqueueInstanceUpgradeAsync(
            request.InstanceId, version.Image, cancellationToken: cancellationToken);

        return new FederationUpgradeResponse(true, version.Image);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/federation/request-upgrade", async (
            FederationUpgradeRequest request,
            RequestUpgradeHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var instanceId = long.Parse(httpContext.User.FindFirst("sub")!.Value);
            var command = new FederationUpgradeCommand(instanceId, request.TargetVersion);
            return await handler.ExecuteAsync(command, ct,
                success => Results.Accepted(null, success));
        })
        .RequireAuthorization(Policies.Federation)
        .Produces<FederationUpgradeResponse>(202)
        .WithName("FederationRequestUpgrade")
        .WithTags("Federation", "Upgrades");
    }
}
