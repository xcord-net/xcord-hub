using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Instances;

public sealed record ListInstancesQuery(
    int Limit = 25,
    string? Cursor = null
);

public sealed record ListInstancesResponse(List<InstanceSummary> Instances, string? NextCursor = null);

public sealed record InstanceSummary(
    string InstanceId,
    string Domain,
    string DisplayName,
    string Status,
    string Tier,
    bool MediaEnabled,
    DateTimeOffset CreatedAt
);

public sealed class ListInstancesHandler(
    HubDbContext dbContext,
    ICurrentUserService currentUserService,
    ICursorService cursorService)
    : IRequestHandler<ListInstancesQuery, Result<ListInstancesResponse>>
{
    public async Task<Result<ListInstancesResponse>> Handle(ListInstancesQuery request, CancellationToken cancellationToken)
    {
        var userIdResult = currentUserService.GetCurrentUserId();
        if (userIdResult.IsFailure) return userIdResult.Error!;
        var userId = userIdResult.Value;

        // Decode opaque cursor (returns null when no cursor was supplied)
        var cursorResult = cursorService.Decode(request.Cursor);
        if (cursorResult.IsFailure) return cursorResult.Error;
        var beforeId = cursorResult.Value;

        var limit = Math.Clamp(request.Limit, 1, 100);

        var query = dbContext.ManagedInstances
            .Include(i => i.Billing)
            .Where(i => i.OwnerId == userId && i.DeletedAt == null);

        if (beforeId.HasValue)
        {
            query = query.Where(i => i.Id < beforeId.Value);
        }

        // Project Id alongside the summary so we can build the next cursor
        // without re-parsing the (now opaque) InstanceId string.
        var rows = await query
            .OrderByDescending(i => i.Id)
            .Take(limit)
            .Select(i => new
            {
                i.Id,
                Summary = new InstanceSummary(
                    i.Id.ToString(),
                    i.Domain,
                    i.DisplayName,
                    i.Status.ToString(),
                    i.Billing!.Tier.ToString(),
                    i.Billing.MediaEnabled,
                    i.CreatedAt
                )
            })
            .ToListAsync(cancellationToken);

        var instances = rows.Select(r => r.Summary).ToList();
        var nextCursor = rows.Count == limit && rows.Count > 0
            ? cursorService.Encode(rows[^1].Id)
            : null;

        return new ListInstancesResponse(instances, nextCursor);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/hub/instances", async (
            ListInstancesHandler handler,
            int? limit,
            string? cursor,
            CancellationToken ct) =>
        {
            var query = new ListInstancesQuery(
                Limit: limit ?? 25,
                Cursor: cursor
            );
            return await handler.ExecuteAsync(query, ct);
        })
        .RequireAuthorization(Policies.User)
        .Produces<ListInstancesResponse>(200)
        .WithName("ListInstances")
        .WithTags("Instances");
    }
}
