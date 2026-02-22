using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Discovery;

public sealed record GetInstanceQuery(long InstanceId);

public sealed record GetInstanceResponse(
    string Id,
    string Name,
    string Description,
    string IconUrl,
    string Domain,
    int MemberCount,
    int OnlineCount,
    DateTimeOffset CreatedAt
);

public sealed class GetInstanceHandler(HubDbContext dbContext)
    : IRequestHandler<GetInstanceQuery, Result<GetInstanceResponse>>
{
    public async Task<Result<GetInstanceResponse>> Handle(GetInstanceQuery request, CancellationToken cancellationToken)
    {
        var raw = await dbContext.ManagedInstances
            .Where(i => i.Id == request.InstanceId && i.Status == InstanceStatus.Running)
            .Select(i => new
            {
                i.Id,
                i.DisplayName,
                Description = i.Description ?? string.Empty,
                IconUrl = i.IconUrl ?? string.Empty,
                i.Domain,
                i.MemberCount,
                i.OnlineCount,
                i.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (raw == null)
        {
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found or not available");
        }

        return new GetInstanceResponse(
            raw.Id.ToString(),
            raw.DisplayName,
            raw.Description,
            raw.IconUrl,
            raw.Domain,
            raw.MemberCount,
            raw.OnlineCount,
            raw.CreatedAt
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/discover/instances/{instanceId:long}", async (
            long instanceId,
            GetInstanceHandler handler,
            CancellationToken ct) =>
        {
            var query = new GetInstanceQuery(instanceId);
            return await handler.ExecuteAsync(query, ct);
        })
        .AllowAnonymous()
        .Produces<GetInstanceResponse>(200)
        .WithName("DiscoveryGetInstance")
        .WithTags("Discovery");
    }
}
