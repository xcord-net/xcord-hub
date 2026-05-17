using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Instances;

public sealed record UpdateFeatureFlagsCommand(
    long InstanceId,
    bool CanUseVoiceChannels,
    bool CanUseVideoChannels,
    bool CanUseSimulcast,
    bool CanUseMemberTiers,
    bool CanBroadcast
);

public sealed record UpdateFeatureFlagsResponse(
    string InstanceId,
    string Message
);

public sealed record UpdateFeatureFlagsRequest(
    bool CanUseVoiceChannels,
    bool CanUseVideoChannels,
    bool CanUseSimulcast,
    bool CanUseMemberTiers,
    bool CanBroadcast
);

public sealed class UpdateFeatureFlagsHandler(HubDbContext dbContext)
    : IRequestHandler<UpdateFeatureFlagsCommand, Result<UpdateFeatureFlagsResponse>>
{
    public async Task<Result<UpdateFeatureFlagsResponse>> Handle(UpdateFeatureFlagsCommand request, CancellationToken cancellationToken)
    {
        var instance = await dbContext.ManagedInstances
            .Include(i => i.Config)
            .FirstOrDefaultAsync(i => i.Id == request.InstanceId, cancellationToken);

        if (instance == null)
        {
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");
        }

        if (instance.Config == null)
        {
            return Error.NotFound("INSTANCE_CONFIG_NOT_FOUND", "Instance configuration not found");
        }

        var featureFlags = new FeatureFlags
        {
            CanUseVoiceChannels = request.CanUseVoiceChannels,
            CanUseVideoChannels = request.CanUseVideoChannels,
            CanUseSimulcast = request.CanUseSimulcast,
            CanUseMemberTiers = request.CanUseMemberTiers,
            CanBroadcast = request.CanBroadcast
        };

        instance.Config.FeatureFlagsJson = JsonSerializer.Serialize(featureFlags);
        instance.Config.UpdatedAt = DateTimeOffset.UtcNow;
        instance.Config.Version++;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new UpdateFeatureFlagsResponse(
            request.InstanceId.ToString(),
            "Feature flags updated successfully"
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPatch("/api/v1/admin/instances/{id}/feature-flags", async (
            long id,
            UpdateFeatureFlagsRequest request,
            UpdateFeatureFlagsHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            // Check if user is admin
            var isAdmin = httpContext.User.HasClaim(c => c.Type == "admin" && c.Value == "true");
            if (!isAdmin)
            {
                return Results.Problem(
                    statusCode: 403,
                    title: "FORBIDDEN",
                    detail: "Admin access required");
            }

            var command = new UpdateFeatureFlagsCommand(
                id,
                request.CanUseVoiceChannels,
                request.CanUseVideoChannels,
                request.CanUseSimulcast,
                request.CanUseMemberTiers,
                request.CanBroadcast
            );

            var result = await handler.Handle(command, ct).ConfigureAwait(false);

            return result.Match(
                success => Results.Ok(success),
                error => Results.Problem(
                    statusCode: error.StatusCode,
                    title: error.Code,
                    detail: error.Message));
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<UpdateFeatureFlagsResponse>(200)
        .WithName("UpdateFeatureFlags")
        .WithTags("Admin", "Instances");
    }
}
