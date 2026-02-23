using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Instances;

public sealed record ListInstancesQuery(
    int Limit = 25,
    long? Before = null
);

public sealed record ListInstancesResponse(List<InstanceSummary> Instances);

public sealed record InstanceSummary(
    string InstanceId,
    string Domain,
    string DisplayName,
    string Status,
    string FeatureTier,
    string UserCountTier,
    DateTimeOffset CreatedAt
);

public sealed class ListInstancesHandler(HubDbContext dbContext, ICurrentUserService currentUserService)
    : IRequestHandler<ListInstancesQuery, Result<ListInstancesResponse>>
{
    public async Task<Result<ListInstancesResponse>> Handle(ListInstancesQuery request, CancellationToken cancellationToken)
    {
        var userIdResult = currentUserService.GetCurrentUserId();
        if (userIdResult.IsFailure) return userIdResult.Error!;
        var userId = userIdResult.Value;

        var limit = Math.Clamp(request.Limit, 1, 100);

        var query = dbContext.ManagedInstances
            .Include(i => i.Billing)
            .Where(i => i.OwnerId == userId && i.DeletedAt == null);

        if (request.Before.HasValue)
        {
            query = query.Where(i => i.Id < request.Before.Value);
        }

        var instances = await query
            .OrderByDescending(i => i.Id)
            .Take(limit)
            .Select(i => new InstanceSummary(
                i.Id.ToString(),
                i.Domain,
                i.DisplayName,
                i.Status.ToString(),
                i.Billing!.FeatureTier.ToString(),
                i.Billing.UserCountTier.ToString(),
                i.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return new ListInstancesResponse(instances);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/hub/instances", async (
            ListInstancesHandler handler,
            int? limit,
            long? before,
            CancellationToken ct) =>
        {
            var query = new ListInstancesQuery(
                Limit: limit ?? 25,
                Before: before
            );
            return await handler.ExecuteAsync(query, ct);
        })
        .RequireAuthorization(Policies.User)
        .Produces<ListInstancesResponse>(200)
        .WithName("ListInstances")
        .WithTags("Instances");
    }
}
