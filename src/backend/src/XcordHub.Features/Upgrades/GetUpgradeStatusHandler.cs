using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Upgrades;

public sealed record GetUpgradeStatusQuery(long Id);

public sealed record UpgradeEventItem(
    string Id,
    string ManagedInstanceId,
    string Status,
    string? PreviousImage,
    string TargetImage,
    string? PreviousVersion,
    string? NewVersion,
    string? ErrorMessage,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt
);

public sealed record GetUpgradeStatusResponse(
    string Id,
    string ToImage,
    string? FromImage,
    string? TargetPool,
    string Status,
    int TotalInstances,
    int CompletedInstances,
    int FailedInstances,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    List<UpgradeEventItem> Events
);

public sealed class GetUpgradeStatusHandler(HubDbContext dbContext)
    : IRequestHandler<GetUpgradeStatusQuery, Result<GetUpgradeStatusResponse>>
{
    public async Task<Result<GetUpgradeStatusResponse>> Handle(
        GetUpgradeStatusQuery request, CancellationToken cancellationToken)
    {
        var rollout = await dbContext.UpgradeRollouts
            .Include(r => r.UpgradeEvents)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (rollout is null)
            return Error.NotFound("ROLLOUT_NOT_FOUND", "Upgrade rollout not found");

        var events = rollout.UpgradeEvents
            .OrderBy(e => e.StartedAt)
            .Select(e => new UpgradeEventItem(
                e.Id.ToString(),
                e.ManagedInstanceId.ToString(),
                e.Status.ToString(),
                e.PreviousImage,
                e.TargetImage,
                e.PreviousVersion,
                e.NewVersion,
                e.ErrorMessage,
                e.StartedAt,
                e.CompletedAt
            ))
            .ToList();

        return new GetUpgradeStatusResponse(
            rollout.Id.ToString(),
            rollout.ToImage,
            rollout.FromImage,
            rollout.TargetPool,
            rollout.Status.ToString(),
            rollout.TotalInstances,
            rollout.CompletedInstances,
            rollout.FailedInstances,
            rollout.StartedAt,
            rollout.CompletedAt,
            events
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/admin/upgrades/{id}", async (
            long id,
            GetUpgradeStatusHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetUpgradeStatusQuery(id), ct);
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<GetUpgradeStatusResponse>(200)
        .WithName("GetUpgradeStatus")
        .WithTags("Admin", "Upgrades");
    }
}
