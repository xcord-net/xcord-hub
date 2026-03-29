using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Billing;

public sealed record GetInstanceUsageQuery(long InstanceId);

public sealed record UptimeIntervalDto(
    string IntervalId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    double DurationMinutes,
    bool IsOpen
);

public sealed record GetInstanceUsageResponse(
    string InstanceId,
    string Domain,
    string Tier,
    bool IsMeteredBilling,
    double TotalUptimeMinutes,
    double TotalUptimeHours,
    int UptimePercentage,
    long EstimatedCostCents,
    List<UptimeIntervalDto> Intervals
);

public sealed class GetInstanceUsageHandler(
    HubDbContext dbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetInstanceUsageQuery, Result<GetInstanceUsageResponse>>
{
    // Enterprise metered rate: $0.01 per minute uptime ($0.60/hour)
    // This is approximately $432/month at 100% uptime - enterprise grade pricing
    private const decimal CentsPerMinute = 1m;

    public async Task<Result<GetInstanceUsageResponse>> Handle(
        GetInstanceUsageQuery request,
        CancellationToken cancellationToken)
    {
        var userIdResult = currentUserService.GetCurrentUserId();
        if (userIdResult.IsFailure) return userIdResult.Error!;
        var userId = userIdResult.Value;

        var instance = await dbContext.ManagedInstances
            .Include(i => i.Billing)
            .Where(i => i.Id == request.InstanceId && i.DeletedAt == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (instance == null)
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");

        if (instance.OwnerId != userId)
            return Error.Forbidden("FORBIDDEN", "You do not own this instance");

        if (instance.Billing == null)
            return Error.NotFound("BILLING_NOT_FOUND", "Billing record not found for this instance");

        // Fetch uptime intervals for the current billing period (last 30 days)
        var periodStart = DateTimeOffset.UtcNow.AddDays(-30);
        var now = DateTimeOffset.UtcNow;

        var intervals = await dbContext.UptimeIntervals
            .Where(u =>
                u.ManagedInstanceId == instance.Id &&
                u.StartedAt >= periodStart)
            .OrderByDescending(u => u.StartedAt)
            .ToListAsync(cancellationToken);

        // Build DTOs - open intervals use current time as their effective end
        var intervalDtos = intervals.Select(u =>
        {
            var effectiveEnd = u.EndedAt ?? now;
            var duration = (effectiveEnd - u.StartedAt).TotalMinutes;
            return new UptimeIntervalDto(
                IntervalId: u.Id.ToString(),
                StartedAt: u.StartedAt,
                EndedAt: u.EndedAt,
                DurationMinutes: Math.Round(duration, 2),
                IsOpen: u.EndedAt == null
            );
        }).ToList();

        var totalMinutes = intervalDtos.Sum(i => i.DurationMinutes);
        var totalHours = Math.Round(totalMinutes / 60, 2);

        // Uptime percentage relative to the period
        var periodMinutes = (now - periodStart).TotalMinutes;
        var uptimePct = periodMinutes > 0
            ? (int)Math.Round(totalMinutes / periodMinutes * 100)
            : 0;
        uptimePct = Math.Min(100, Math.Max(0, uptimePct));

        // Cost estimate for metered instances
        var estimatedCostCents = instance.Billing.IsMeteredBilling
            ? (long)Math.Round((decimal)totalMinutes * CentsPerMinute)
            : TierDefaults.GetTotalPriceCents(instance.Billing.Tier, instance.Billing.MediaEnabled);

        return new GetInstanceUsageResponse(
            InstanceId: instance.Id.ToString(),
            Domain: instance.Domain,
            Tier: instance.Billing.Tier.ToString(),
            IsMeteredBilling: instance.Billing.IsMeteredBilling,
            TotalUptimeMinutes: Math.Round(totalMinutes, 2),
            TotalUptimeHours: totalHours,
            UptimePercentage: uptimePct,
            EstimatedCostCents: estimatedCostCents,
            Intervals: intervalDtos
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/hub/instances/{id}/usage", async (
            long id,
            GetInstanceUsageHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetInstanceUsageQuery(id), ct);
        })
        .RequireAuthorization(Policies.User)
        .Produces<GetInstanceUsageResponse>(200)
        .WithName("GetInstanceUsage")
        .WithTags("Billing");
    }
}
