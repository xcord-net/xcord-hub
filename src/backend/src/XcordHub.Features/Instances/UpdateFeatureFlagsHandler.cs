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
    bool CanCreateBots,
    bool CanUseWebhooks,
    bool CanUseCustomEmoji,
    bool CanUseThreads,
    bool CanUseVoiceChannels,
    bool CanUseVideoChannels,
    bool CanUseForumChannels,
    bool CanUseScheduledEvents
);

public sealed record UpdateFeatureFlagsResponse(
    long InstanceId,
    string Message
);

public sealed record UpdateFeatureFlagsRequest(
    bool CanCreateBots,
    bool CanUseWebhooks,
    bool CanUseCustomEmoji,
    bool CanUseThreads,
    bool CanUseVoiceChannels,
    bool CanUseVideoChannels,
    bool CanUseForumChannels,
    bool CanUseScheduledEvents
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
            CanCreateBots = request.CanCreateBots,
            CanUseWebhooks = request.CanUseWebhooks,
            CanUseCustomEmoji = request.CanUseCustomEmoji,
            CanUseThreads = request.CanUseThreads,
            CanUseVoiceChannels = request.CanUseVoiceChannels,
            CanUseVideoChannels = request.CanUseVideoChannels,
            CanUseForumChannels = request.CanUseForumChannels,
            CanUseScheduledEvents = request.CanUseScheduledEvents
        };

        instance.Config.FeatureFlagsJson = JsonSerializer.Serialize(featureFlags);
        instance.Config.UpdatedAt = DateTimeOffset.UtcNow;
        instance.Config.Version++;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpdateFeatureFlagsResponse(
            request.InstanceId,
            "Feature flags updated successfully"
        );
    }

}

public static class UpdateFeatureFlagsEndpoint
{
    public static void MapUpdateFeatureFlagsEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/api/v1/admin/instances/{id}/feature-flags", async (
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
                request.CanCreateBots,
                request.CanUseWebhooks,
                request.CanUseCustomEmoji,
                request.CanUseThreads,
                request.CanUseVoiceChannels,
                request.CanUseVideoChannels,
                request.CanUseForumChannels,
                request.CanUseScheduledEvents
            );

            var result = await handler.Handle(command, ct);

            return result.Match(
                success => Results.Ok(new
                {
                    instanceId = success.InstanceId,
                    message = success.Message
                }),
                error => Results.Problem(
                    statusCode: error.StatusCode,
                    title: error.Code,
                    detail: error.Message));
        })
        .RequireAuthorization(Policies.Admin)
        .WithName("UpdateFeatureFlags")
        .WithTags("Admin", "Instances");
    }
}
