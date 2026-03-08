using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Upgrades;

public sealed record ListUpgradesQuery(int Page = 1, int PageSize = 25);

public sealed record UpgradeRolloutListItem(
    string Id,
    string ToImage,
    string? FromImage,
    string? TargetPool,
    string Status,
    int TotalInstances,
    int CompletedInstances,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt
);

public sealed record ListUpgradesResponse(
    List<UpgradeRolloutListItem> Rollouts,
    int Total,
    int Page,
    int PageSize
);

public sealed class ListUpgradesHandler(HubDbContext dbContext)
    : IRequestHandler<ListUpgradesQuery, Result<ListUpgradesResponse>>
{
    public async Task<Result<ListUpgradesResponse>> Handle(
        ListUpgradesQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var total = await dbContext.UpgradeRollouts.CountAsync(cancellationToken);

        var rollouts = await dbContext.UpgradeRollouts
            .OrderByDescending(r => r.StartedAt)
            .Skip(skip)
            .Take(pageSize)
            .Select(r => new UpgradeRolloutListItem(
                r.Id.ToString(),
                r.ToImage,
                r.FromImage,
                r.TargetPool,
                r.Status.ToString(),
                r.TotalInstances,
                r.CompletedInstances,
                r.StartedAt,
                r.CompletedAt
            ))
            .ToListAsync(cancellationToken);

        return new ListUpgradesResponse(rollouts, total, page, pageSize);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/admin/upgrades", async (
            int? page,
            int? pageSize,
            ListUpgradesHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new ListUpgradesQuery(page ?? 1, pageSize ?? 25), ct);
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<ListUpgradesResponse>(200)
        .WithName("ListUpgrades")
        .WithTags("Admin", "Upgrades");
    }
}
