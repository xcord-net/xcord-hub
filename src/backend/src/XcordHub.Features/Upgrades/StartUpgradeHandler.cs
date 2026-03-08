using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Upgrades;

public sealed record StartUpgradeCommand(
    string ToImage,
    string? FromImage = null,
    string? TargetPool = null,
    bool Force = false,
    long InitiatedBy = 0
);

public sealed record StartUpgradeResponse(
    string Id,
    string ToImage,
    string? FromImage,
    string? TargetPool,
    string Status,
    DateTimeOffset StartedAt
);

public sealed class StartUpgradeHandler(
    HubDbContext dbContext,
    SnowflakeId snowflakeGenerator,
    IUpgradeQueue upgradeQueue)
    : IRequestHandler<StartUpgradeCommand, Result<StartUpgradeResponse>>,
      IValidatable<StartUpgradeCommand>
{
    public Error? Validate(StartUpgradeCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.ToImage))
            return Error.Validation("VALIDATION_FAILED", "ToImage is required");

        return null;
    }

    public async Task<Result<StartUpgradeResponse>> Handle(
        StartUpgradeCommand request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var rollout = new UpgradeRollout
        {
            Id = snowflakeGenerator.NextId(),
            FromImage = request.FromImage,
            ToImage = request.ToImage,
            TargetPool = request.TargetPool,
            Status = RolloutStatus.Pending,
            StartedAt = now,
            InitiatedBy = request.InitiatedBy
        };

        dbContext.UpgradeRollouts.Add(rollout);
        await dbContext.SaveChangesAsync(cancellationToken);

        await upgradeQueue.EnqueueRolloutAsync(rollout.Id, request.Force, cancellationToken);

        return new StartUpgradeResponse(
            rollout.Id.ToString(),
            rollout.ToImage,
            rollout.FromImage,
            rollout.TargetPool,
            rollout.Status.ToString(),
            rollout.StartedAt
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/admin/upgrades", async (
            StartUpgradeCommand command,
            StartUpgradeHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = long.Parse(httpContext.User.FindFirst("sub")!.Value);
            return await handler.ExecuteAsync(command with { InitiatedBy = userId }, ct,
                success => Results.Accepted($"/api/v1/admin/upgrades/{success.Id}", success));
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<StartUpgradeResponse>(202)
        .WithName("StartUpgrade")
        .WithTags("Admin", "Upgrades");
    }
}
