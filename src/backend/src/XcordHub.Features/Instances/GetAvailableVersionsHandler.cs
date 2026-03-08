using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Instances;

public sealed record GetAvailableVersionsQuery(long InstanceId, long UserId);

public sealed record AvailableVersionDto(
    string Id,
    string Version,
    string Image,
    string? ReleaseNotes,
    bool IsMinimumVersion,
    DateTimeOffset? MinimumEnforcementDate,
    DateTimeOffset PublishedAt);

public sealed record GetAvailableVersionsResponse(List<AvailableVersionDto> Versions);

public sealed class GetAvailableVersionsHandler(HubDbContext dbContext)
    : IRequestHandler<GetAvailableVersionsQuery, Result<GetAvailableVersionsResponse>>
{
    public async Task<Result<GetAvailableVersionsResponse>> Handle(
        GetAvailableVersionsQuery request, CancellationToken cancellationToken)
    {
        var instance = await dbContext.ManagedInstances
            .Include(i => i.Infrastructure)
            .FirstOrDefaultAsync(i => i.Id == request.InstanceId && i.DeletedAt == null, cancellationToken);

        if (instance == null)
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");

        if (instance.OwnerId != request.UserId)
            return Error.Forbidden("NOT_OWNER", "You do not have permission to manage this instance");

        var versions = await dbContext.AvailableVersions
            .Where(v => v.DeletedAt == null)
            .OrderByDescending(v => v.PublishedAt)
            .Select(v => new AvailableVersionDto(
                v.Id.ToString(),
                v.Version,
                v.Image,
                v.ReleaseNotes,
                v.IsMinimumVersion,
                v.MinimumEnforcementDate,
                v.PublishedAt))
            .ToListAsync(cancellationToken);

        return new GetAvailableVersionsResponse(versions);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/hub/instances/{instanceId:long}/versions", async (
            [FromRoute] long instanceId,
            ClaimsPrincipal user,
            GetAvailableVersionsHandler handler,
            CancellationToken ct) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null || !long.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            var query = new GetAvailableVersionsQuery(instanceId, userId);
            return await handler.ExecuteAsync(query, ct);
        })
        .RequireAuthorization(Policies.User)
        .Produces<GetAvailableVersionsResponse>(200)
        .WithName("GetAvailableVersions")
        .WithTags("Instances", "Upgrades");
    }
}
