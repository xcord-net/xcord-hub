using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Upgrades;

public sealed record CancelUpgradeCommand(long Id);

public sealed record CancelUpgradeResponse(
    string Id,
    string Status,
    DateTimeOffset? CompletedAt
);

public sealed class CancelUpgradeHandler(HubDbContext dbContext)
    : IRequestHandler<CancelUpgradeCommand, Result<CancelUpgradeResponse>>
{
    public async Task<Result<CancelUpgradeResponse>> Handle(
        CancelUpgradeCommand request, CancellationToken cancellationToken)
    {
        var rollout = await dbContext.UpgradeRollouts
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (rollout is null)
            return Error.NotFound("ROLLOUT_NOT_FOUND", "Upgrade rollout not found");

        if (rollout.Status is not (RolloutStatus.Pending or RolloutStatus.InProgress or RolloutStatus.Paused))
            return Error.BadRequest("INVALID_STATUS", $"Cannot cancel a rollout with status '{rollout.Status}'");

        rollout.Status = RolloutStatus.Cancelled;
        rollout.CompletedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CancelUpgradeResponse(
            rollout.Id.ToString(),
            rollout.Status.ToString(),
            rollout.CompletedAt
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/admin/upgrades/{id}/cancel", async (
            long id,
            CancelUpgradeHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new CancelUpgradeCommand(id), ct);
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<CancelUpgradeResponse>(200)
        .WithName("CancelUpgrade")
        .WithTags("Admin", "Upgrades");
    }
}
