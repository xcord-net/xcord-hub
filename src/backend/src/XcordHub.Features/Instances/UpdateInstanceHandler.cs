using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Instances;

public sealed record UpdateInstanceCommand(long InstanceId, string DisplayName);

public sealed class UpdateInstanceHandler(HubDbContext dbContext, ICurrentUserService currentUserService)
    : IRequestHandler<UpdateInstanceCommand, Result<GetInstanceResponse>>
{
    public async Task<Result<GetInstanceResponse>> Handle(UpdateInstanceCommand request, CancellationToken cancellationToken)
    {
        var userIdResult = currentUserService.GetCurrentUserId();
        if (userIdResult.IsFailure) return userIdResult.Error!;
        var userId = userIdResult.Value;

        var instance = await dbContext.ManagedInstances
            .Include(i => i.Billing)
            .FirstOrDefaultAsync(i => i.Id == request.InstanceId && i.DeletedAt == null && i.OwnerId == userId, cancellationToken);

        if (instance is null)
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
            instance.DisplayName = request.DisplayName.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);

        var subdomain = instance.Domain.Contains('.')
            ? instance.Domain[..instance.Domain.IndexOf('.')]
            : instance.Domain;

        return new GetInstanceResponse(
            instance.Id.ToString(),
            subdomain,
            instance.DisplayName,
            instance.Domain,
            instance.Status.ToString(),
            instance.Billing?.Tier.ToString() ?? "Free",
            instance.Billing?.MediaEnabled ?? false,
            instance.CreatedAt
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPatch("/api/v1/hub/instances/{instanceId:long}", async (
            long instanceId,
            UpdateInstanceRequest body,
            UpdateInstanceHandler handler,
            CancellationToken ct) =>
        {
            var command = new UpdateInstanceCommand(instanceId, body.DisplayName ?? string.Empty);
            return await handler.ExecuteAsync(command, ct);
        })
        .RequireAuthorization(Policies.User)
        .Produces<GetInstanceResponse>(200)
        .WithName("UpdateInstance")
        .WithTags("Instances");
    }
}

public sealed record UpdateInstanceRequest(string? DisplayName);
