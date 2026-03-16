using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Federation;

public sealed record UpdateBatchPreferenceRequest(bool Enabled);

public sealed record UpdateBatchPreferenceCommand(long InstanceId, bool Enabled);

public sealed record UpdateBatchPreferenceResponse(bool BatchUpgradesEnabled);

public sealed class UpdateBatchPreferenceHandler(HubDbContext dbContext)
    : IRequestHandler<UpdateBatchPreferenceCommand, Result<UpdateBatchPreferenceResponse>>
{
    public async Task<Result<UpdateBatchPreferenceResponse>> Handle(
        UpdateBatchPreferenceCommand request, CancellationToken cancellationToken)
    {
        var instance = await dbContext.ManagedInstances
            .Include(i => i.Config)
            .FirstOrDefaultAsync(i => i.Id == request.InstanceId && i.DeletedAt == null, cancellationToken);

        if (instance == null)
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");

        if (instance.Config == null)
            return Error.NotFound("CONFIG_NOT_FOUND", "Instance configuration not found");

        instance.Config.BatchUpgradesEnabled = request.Enabled;
        instance.Config.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpdateBatchPreferenceResponse(instance.Config.BatchUpgradesEnabled);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPatch("/api/v1/federation/batch-upgrades", async (
            UpdateBatchPreferenceRequest request,
            UpdateBatchPreferenceHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var instanceId = long.Parse(httpContext.User.FindFirst("sub")!.Value);
            return await handler.ExecuteAsync(
                new UpdateBatchPreferenceCommand(instanceId, request.Enabled), ct);
        })
        .RequireAuthorization(Policies.Federation)
        .Produces<UpdateBatchPreferenceResponse>(200)
        .WithName("UpdateBatchPreference")
        .WithTags("Federation", "Upgrades");
    }
}
