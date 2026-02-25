using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Instances;

public sealed record UpdateResourceLimitsCommand(
    long InstanceId,
    int MaxUsers,
    int MaxServers,
    int MaxStorageMb,
    int MaxCpuPercent,
    int MaxMemoryMb,
    int MaxRateLimit,
    int MaxVoiceConcurrency = 0,
    int MaxVideoConcurrency = 0
);

public sealed record UpdateResourceLimitsResponse(
    string InstanceId,
    string Message
);

public sealed record UpdateResourceLimitsRequest(
    int MaxUsers,
    int MaxServers,
    int MaxStorageMb,
    int MaxCpuPercent,
    int MaxMemoryMb,
    int MaxRateLimit,
    int MaxVoiceConcurrency = 0,
    int MaxVideoConcurrency = 0
);

public sealed class UpdateResourceLimitsHandler(HubDbContext dbContext)
    : IRequestHandler<UpdateResourceLimitsCommand, Result<UpdateResourceLimitsResponse>>
{
    public async Task<Result<UpdateResourceLimitsResponse>> Handle(UpdateResourceLimitsCommand request, CancellationToken cancellationToken)
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

        var resourceLimits = new ResourceLimits
        {
            MaxUsers = request.MaxUsers,
            MaxServers = request.MaxServers,
            MaxStorageMb = request.MaxStorageMb,
            MaxCpuPercent = request.MaxCpuPercent,
            MaxMemoryMb = request.MaxMemoryMb,
            MaxRateLimit = request.MaxRateLimit,
            MaxVoiceConcurrency = request.MaxVoiceConcurrency,
            MaxVideoConcurrency = request.MaxVideoConcurrency
        };

        instance.Config.ResourceLimitsJson = JsonSerializer.Serialize(resourceLimits);
        instance.Config.UpdatedAt = DateTimeOffset.UtcNow;
        instance.Config.Version++;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpdateResourceLimitsResponse(
            request.InstanceId.ToString(),
            "Resource limits updated successfully"
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPatch("/api/v1/admin/instances/{id}/resource-limits", async (
            long id,
            UpdateResourceLimitsRequest request,
            UpdateResourceLimitsHandler handler,
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

            var command = new UpdateResourceLimitsCommand(
                id,
                request.MaxUsers,
                request.MaxServers,
                request.MaxStorageMb,
                request.MaxCpuPercent,
                request.MaxMemoryMb,
                request.MaxRateLimit,
                request.MaxVoiceConcurrency,
                request.MaxVideoConcurrency
            );

            var result = await handler.Handle(command, ct);

            return result.Match(
                success => Results.Ok(success),
                error => Results.Problem(
                    statusCode: error.StatusCode,
                    title: error.Code,
                    detail: error.Message));
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<UpdateResourceLimitsResponse>(200)
        .WithName("UpdateResourceLimits")
        .WithTags("Admin", "Instances");
    }
}
