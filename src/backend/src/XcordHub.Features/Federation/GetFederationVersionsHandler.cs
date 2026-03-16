using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Federation;

public sealed record GetFederationVersionsQuery(long InstanceId);

public sealed record FederationVersionItem(
    string Id,
    string Version,
    string Image,
    string? ReleaseNotes,
    bool IsMinimumVersion,
    DateTimeOffset? MinimumEnforcementDate,
    DateTimeOffset PublishedAt
);

public sealed record GetFederationVersionsResponse(
    List<FederationVersionItem> Versions,
    string? CurrentVersion,
    bool BatchUpgradesEnabled
);

public sealed class GetFederationVersionsHandler(HubDbContext dbContext)
    : IRequestHandler<GetFederationVersionsQuery, Result<GetFederationVersionsResponse>>
{
    public async Task<Result<GetFederationVersionsResponse>> Handle(
        GetFederationVersionsQuery request, CancellationToken cancellationToken)
    {
        var instance = await dbContext.ManagedInstances
            .Include(i => i.Health)
            .Include(i => i.Config)
            .FirstOrDefaultAsync(i => i.Id == request.InstanceId && i.DeletedAt == null, cancellationToken);

        if (instance == null)
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");

        var versions = await dbContext.AvailableVersions
            .Where(v => v.DeletedAt == null)
            .OrderByDescending(v => v.PublishedAt)
            .Select(v => new FederationVersionItem(
                v.Id.ToString(),
                v.Version,
                v.Image,
                v.ReleaseNotes,
                v.IsMinimumVersion,
                v.MinimumEnforcementDate,
                v.PublishedAt
            ))
            .ToListAsync(cancellationToken);

        return new GetFederationVersionsResponse(
            versions,
            instance.Health?.Version,
            instance.Config?.BatchUpgradesEnabled ?? true
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/federation/versions", async (
            GetFederationVersionsHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var instanceId = long.Parse(httpContext.User.FindFirst("sub")!.Value);
            return await handler.ExecuteAsync(new GetFederationVersionsQuery(instanceId), ct);
        })
        .RequireAuthorization(Policies.Federation)
        .Produces<GetFederationVersionsResponse>(200)
        .WithName("GetFederationVersions")
        .WithTags("Federation", "Upgrades");
    }
}
