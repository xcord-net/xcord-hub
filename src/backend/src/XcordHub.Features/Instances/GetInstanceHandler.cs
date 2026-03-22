using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Instances;

public sealed record GetInstanceQuery(long InstanceId);

public sealed record GetInstanceResponse(
    string Id,
    string Subdomain,
    string DisplayName,
    string Domain,
    string Status,
    string Tier,
    bool MediaEnabled,
    DateTimeOffset CreatedAt
);

public sealed class GetInstanceHandler(HubDbContext dbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetInstanceQuery, Result<GetInstanceResponse>>
{
    public async Task<Result<GetInstanceResponse>> Handle(GetInstanceQuery request, CancellationToken cancellationToken)
    {
        var userIdResult = currentUserService.GetCurrentUserId();
        if (userIdResult.IsFailure) return userIdResult.Error!;
        var userId = userIdResult.Value;

        var instance = await dbContext.ManagedInstances
            .Include(i => i.Billing)
            .FirstOrDefaultAsync(i => i.Id == request.InstanceId && i.DeletedAt == null && i.OwnerId == userId, cancellationToken);

        if (instance is null)
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");

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
        return app.MapGet("/api/v1/hub/instances/{instanceId:long}", async (
            long instanceId,
            GetInstanceHandler handler,
            CancellationToken ct) =>
        {
            var query = new GetInstanceQuery(instanceId);
            return await handler.ExecuteAsync(query, ct);
        })
        .RequireAuthorization(Policies.User)
        .Produces<GetInstanceResponse>(200)
        .WithName("GetInstance")
        .WithTags("Instances");
    }
}
