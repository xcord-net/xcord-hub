using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Upgrades;

public sealed record ListVersionsQuery;

public sealed record VersionListItem(
    string Id,
    string Version,
    string Image,
    string? ReleaseNotes,
    bool IsMinimumVersion,
    DateTimeOffset? MinimumEnforcementDate,
    DateTimeOffset PublishedAt
);

public sealed record ListVersionsResponse(List<VersionListItem> Versions);

public sealed class ListVersionsHandler(HubDbContext dbContext)
    : IRequestHandler<ListVersionsQuery, Result<ListVersionsResponse>>
{
    public async Task<Result<ListVersionsResponse>> Handle(
        ListVersionsQuery request, CancellationToken cancellationToken)
    {
        var versions = await dbContext.AvailableVersions
            .Where(v => v.DeletedAt == null)
            .OrderByDescending(v => v.PublishedAt)
            .Select(v => new VersionListItem(
                v.Id.ToString(),
                v.Version,
                v.Image,
                v.ReleaseNotes,
                v.IsMinimumVersion,
                v.MinimumEnforcementDate,
                v.PublishedAt
            ))
            .ToListAsync(cancellationToken);

        return new ListVersionsResponse(versions);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/admin/versions", async (
            ListVersionsHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new ListVersionsQuery(), ct);
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<ListVersionsResponse>(200)
        .WithName("ListVersions")
        .WithTags("Admin", "Upgrades");
    }
}
