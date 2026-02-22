using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Admin;

public sealed record AdminGetInstanceQuery(long Id);

public sealed record AdminGetInstanceResponse(
    string Id,
    string Subdomain,
    string DisplayName,
    string Domain,
    string Status,
    string Tier,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SuspendedAt,
    DateTimeOffset? DestroyedAt,
    string OwnerId,
    string OwnerUsername,
    object? ResourceLimits,
    object? FeatureFlags,
    object? Health,
    object? Infrastructure
);

public sealed class AdminGetInstanceHandler(HubDbContext dbContext)
    : IRequestHandler<AdminGetInstanceQuery, Result<AdminGetInstanceResponse>>
{
    public async Task<Result<AdminGetInstanceResponse>> Handle(AdminGetInstanceQuery request, CancellationToken cancellationToken)
    {
        var instance = await dbContext.ManagedInstances
            .Include(i => i.Owner)
            .Include(i => i.Billing)
            .Include(i => i.Config)
            .Include(i => i.Infrastructure)
            .Include(i => i.Health)
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken);

        if (instance is null)
            return Error.NotFound("NOT_FOUND", "Instance not found");

        var subdomain = instance.Domain.Contains('.')
            ? instance.Domain.Substring(0, instance.Domain.IndexOf('.'))
            : instance.Domain;

        object? resourceLimits = null;
        object? featureFlags = null;
        if (instance.Config is not null)
        {
            if (!string.IsNullOrEmpty(instance.Config.ResourceLimitsJson))
                resourceLimits = JsonSerializer.Deserialize<object>(instance.Config.ResourceLimitsJson);
            if (!string.IsNullOrEmpty(instance.Config.FeatureFlagsJson))
                featureFlags = JsonSerializer.Deserialize<object>(instance.Config.FeatureFlagsJson);
        }

        object? health = null;
        if (instance.Health is not null)
        {
            health = new
            {
                isHealthy = instance.Health.IsHealthy,
                lastCheckAt = instance.Health.LastCheckAt,
                consecutiveFailures = instance.Health.ConsecutiveFailures,
                responseTimeMs = instance.Health.ResponseTimeMs,
                errorMessage = instance.Health.ErrorMessage
            };
        }

        object? infrastructure = null;
        if (instance.Infrastructure is not null)
        {
            infrastructure = new
            {
                containerName = instance.Infrastructure.DockerContainerId,
                databaseName = instance.Infrastructure.DatabaseName,
                redisHost = $"redis-db-{instance.Infrastructure.RedisDb}",
                minioBucket = $"instance-{instance.Id}"
            };
        }

        return new AdminGetInstanceResponse(
            instance.Id.ToString(),
            subdomain,
            instance.DisplayName,
            instance.Domain,
            instance.Status.ToString(),
            instance.Billing?.Tier.ToString() ?? "Free",
            instance.CreatedAt,
            null, // SuspendedAt — ManagedInstance does not have a dedicated SuspendedAt field
            instance.DeletedAt,
            instance.OwnerId.ToString(),
            instance.Owner.Username,
            resourceLimits,
            featureFlags,
            health,
            infrastructure
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/admin/instances/{id:long}", async (
            long id,
            AdminGetInstanceHandler handler,
            CancellationToken ct) =>
        {
            var query = new AdminGetInstanceQuery(id);
            return await handler.ExecuteAsync(query, ct);
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<AdminGetInstanceResponse>(200)
        .WithName("AdminGetInstance")
        .WithTags("Admin");
    }
}
