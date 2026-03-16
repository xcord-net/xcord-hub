using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Upgrades;

public sealed record PauseRolloutCommand(long Id);
public sealed record PauseRolloutResponse(string Id, string Status);

public sealed class PauseRolloutHandler(HubDbContext dbContext)
    : IRequestHandler<PauseRolloutCommand, Result<PauseRolloutResponse>>
{
    public async Task<Result<PauseRolloutResponse>> Handle(
        PauseRolloutCommand request, CancellationToken cancellationToken)
    {
        var rollout = await dbContext.UpgradeRollouts
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (rollout is null)
            return Error.NotFound("ROLLOUT_NOT_FOUND", "Upgrade rollout not found");

        if (rollout.Status is not RolloutStatus.InProgress)
            return Error.BadRequest("INVALID_STATUS", $"Cannot pause a rollout with status '{rollout.Status}'");

        rollout.Status = RolloutStatus.Paused;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PauseRolloutResponse(rollout.Id.ToString(), rollout.Status.ToString());
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/admin/upgrades/{id}/pause", async (
            long id,
            PauseRolloutHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new PauseRolloutCommand(id), ct);
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<PauseRolloutResponse>(200)
        .WithName("PauseRollout")
        .WithTags("Admin", "Upgrades");
    }
}
