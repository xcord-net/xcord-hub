using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Federation;

public sealed record GetUpgradeHistoryQuery(long InstanceId);

public sealed record UpgradeHistoryItem(
    string Id,
    string Status,
    string? PreviousVersion,
    string? NewVersion,
    string? PreviousImage,
    string TargetImage,
    string? ErrorMessage,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt
);

public sealed record GetUpgradeHistoryResponse(List<UpgradeHistoryItem> Events);

public sealed class GetUpgradeHistoryHandler(HubDbContext dbContext)
    : IRequestHandler<GetUpgradeHistoryQuery, Result<GetUpgradeHistoryResponse>>
{
    public async Task<Result<GetUpgradeHistoryResponse>> Handle(
        GetUpgradeHistoryQuery request, CancellationToken cancellationToken)
    {
        var events = await dbContext.UpgradeEvents
            .Where(e => e.ManagedInstanceId == request.InstanceId)
            .OrderByDescending(e => e.StartedAt)
            .Take(50)
            .Select(e => new UpgradeHistoryItem(
                e.Id.ToString(),
                e.Status.ToString(),
                e.PreviousVersion,
                e.NewVersion,
                e.PreviousImage,
                e.TargetImage,
                e.ErrorMessage,
                e.StartedAt,
                e.CompletedAt
            ))
            .ToListAsync(cancellationToken);

        return new GetUpgradeHistoryResponse(events);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/federation/upgrade-history", async (
            GetUpgradeHistoryHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var instanceId = long.Parse(httpContext.User.FindFirst("sub")!.Value);
            return await handler.ExecuteAsync(new GetUpgradeHistoryQuery(instanceId), ct);
        })
        .RequireAuthorization(Policies.Federation)
        .Produces<GetUpgradeHistoryResponse>(200)
        .WithName("GetUpgradeHistory")
        .WithTags("Federation", "Upgrades");
    }
}
