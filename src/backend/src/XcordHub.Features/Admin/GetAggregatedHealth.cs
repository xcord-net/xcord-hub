using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Admin;

public sealed record GetAggregatedHealthQuery();

public sealed record InstanceHealthDto(
    string Id,
    string Domain,
    string Status,
    bool IsHealthy,
    int ConsecutiveFailures,
    int? ResponseTimeMs,
    string? ErrorMessage,
    DateTimeOffset? LastCheckAt
);

public sealed record AggregatedHealthResponse(
    string OverallStatus,
    int TotalInstances,
    int HealthyInstances,
    int UnhealthyInstances,
    DateTimeOffset Timestamp,
    IEnumerable<InstanceHealthDto> Instances
);

public sealed class GetAggregatedHealthHandler(HubDbContext dbContext)
    : IRequestHandler<GetAggregatedHealthQuery, Result<AggregatedHealthResponse>>
{
    public async Task<Result<AggregatedHealthResponse>> Handle(GetAggregatedHealthQuery request, CancellationToken cancellationToken)
    {
        var instances = await dbContext.ManagedInstances
            .Include(i => i.Health)
            .Where(i => i.Status == InstanceStatus.Running && i.DeletedAt == null)
            .ToListAsync(cancellationToken);

        var healthDtos = new List<InstanceHealthDto>();

        foreach (var instance in instances)
        {
            if (instance.Health != null)
            {
                healthDtos.Add(new InstanceHealthDto(
                    instance.Id.ToString(),
                    instance.Domain,
                    instance.Status.ToString(),
                    instance.Health.IsHealthy,
                    instance.Health.ConsecutiveFailures,
                    instance.Health.ResponseTimeMs,
                    instance.Health.ErrorMessage,
                    instance.Health.LastCheckAt
                ));
            }
            else
            {
                // No health record yet, consider unknown
                healthDtos.Add(new InstanceHealthDto(
                    instance.Id.ToString(),
                    instance.Domain,
                    instance.Status.ToString(),
                    false,
                    0,
                    0,
                    "No health checks recorded",
                    null
                ));
            }
        }

        var healthyCount = healthDtos.Count(h => h.IsHealthy);
        var unhealthyCount = healthDtos.Count - healthyCount;

        var overallStatus = unhealthyCount == 0 ? "Healthy" : (healthyCount == 0 ? "Unhealthy" : "Degraded");

        return new AggregatedHealthResponse(
            overallStatus,
            healthDtos.Count,
            healthyCount,
            unhealthyCount,
            DateTimeOffset.UtcNow,
            healthDtos
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/admin/health", async (
            GetAggregatedHealthHandler handler,
            CancellationToken ct) =>
        {
            var query = new GetAggregatedHealthQuery();
            return await handler.ExecuteAsync(query, ct);
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<AggregatedHealthResponse>(200)
        .WithName("GetAggregatedHealth")
        .WithTags("Admin");
    }
}
