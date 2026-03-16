using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Upgrades;

public sealed record ResumeRolloutCommand(long Id);
public sealed record ResumeRolloutResponse(string Id, string Status);

public sealed class ResumeRolloutHandler(HubDbContext dbContext, IUpgradeQueue upgradeQueue)
    : IRequestHandler<ResumeRolloutCommand, Result<ResumeRolloutResponse>>
{
    public async Task<Result<ResumeRolloutResponse>> Handle(
        ResumeRolloutCommand request, CancellationToken cancellationToken)
    {
        var rollout = await dbContext.UpgradeRollouts
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (rollout is null)
            return Error.NotFound("ROLLOUT_NOT_FOUND", "Upgrade rollout not found");

        if (rollout.Status is not RolloutStatus.Paused)
            return Error.BadRequest("INVALID_STATUS", $"Cannot resume a rollout with status '{rollout.Status}'");

        rollout.Status = RolloutStatus.InProgress;
        rollout.FailedInstances = 0;
        await dbContext.SaveChangesAsync(cancellationToken);

        await upgradeQueue.EnqueueRolloutAsync(rollout.Id, force: false, cancellationToken);

        return new ResumeRolloutResponse(rollout.Id.ToString(), rollout.Status.ToString());
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/admin/upgrades/{id}/resume", async (
            long id,
            ResumeRolloutHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new ResumeRolloutCommand(id), ct);
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<ResumeRolloutResponse>(200)
        .WithName("ResumeRollout")
        .WithTags("Admin", "Upgrades");
    }
}
