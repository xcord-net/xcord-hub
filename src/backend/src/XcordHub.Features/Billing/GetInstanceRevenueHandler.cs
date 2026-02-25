using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Billing;

public sealed record GetInstanceRevenueQuery(long InstanceId);

public sealed record RevenueSummary(
    string InstanceId,
    int TotalAmountCents,
    int TotalPlatformFeeCents,
    int TotalOwnerPayoutCents,
    int CurrentMonthAmountCents,
    int CurrentMonthPlatformFeeCents,
    int CurrentMonthOwnerPayoutCents,
    string? StripeConnectedAccountId,
    int RevenueSharePercent
);

public sealed class GetInstanceRevenueHandler(
    HubDbContext dbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetInstanceRevenueQuery, Result<RevenueSummary>>
{
    public async Task<Result<RevenueSummary>> Handle(
        GetInstanceRevenueQuery request, CancellationToken cancellationToken)
    {
        var userIdResult = currentUserService.GetCurrentUserId();
        if (userIdResult.IsFailure) return userIdResult.Error!;
        var userId = userIdResult.Value;

        var instance = await dbContext.ManagedInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.InstanceId && i.DeletedAt == null, cancellationToken);

        if (instance == null)
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");

        if (instance.OwnerId != userId)
            return Error.Forbidden("NOT_OWNER", "You do not own this instance");

        var revenueConfig = await dbContext.InstanceRevenueConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ManagedInstanceId == request.InstanceId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var allTime = await dbContext.PlatformRevenues
            .Where(r => r.ManagedInstanceId == request.InstanceId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalAmount = g.Sum(r => r.AmountCents),
                TotalPlatformFee = g.Sum(r => r.PlatformFeeCents),
                TotalOwnerPayout = g.Sum(r => r.OwnerPayoutCents)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var currentMonth = await dbContext.PlatformRevenues
            .Where(r => r.ManagedInstanceId == request.InstanceId && r.CreatedAt >= monthStart)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalAmount = g.Sum(r => r.AmountCents),
                TotalPlatformFee = g.Sum(r => r.PlatformFeeCents),
                TotalOwnerPayout = g.Sum(r => r.OwnerPayoutCents)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new RevenueSummary(
            InstanceId: request.InstanceId.ToString(),
            TotalAmountCents: allTime?.TotalAmount ?? 0,
            TotalPlatformFeeCents: allTime?.TotalPlatformFee ?? 0,
            TotalOwnerPayoutCents: allTime?.TotalOwnerPayout ?? 0,
            CurrentMonthAmountCents: currentMonth?.TotalAmount ?? 0,
            CurrentMonthPlatformFeeCents: currentMonth?.TotalPlatformFee ?? 0,
            CurrentMonthOwnerPayoutCents: currentMonth?.TotalOwnerPayout ?? 0,
            StripeConnectedAccountId: revenueConfig?.StripeConnectedAccountId,
            RevenueSharePercent: revenueConfig?.DefaultRevenueSharePercent ?? 70
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/hub/instances/{instanceId}/revenue", async (
            long instanceId,
            GetInstanceRevenueHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetInstanceRevenueQuery(instanceId), ct);
        })
        .RequireAuthorization(Policies.User)
        .Produces<RevenueSummary>(200)
        .WithName("GetInstanceRevenue")
        .WithTags("Billing");
    }
}
