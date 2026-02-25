using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Billing;

public sealed record GetPlatformRevenueQuery();

public sealed record PlatformRevenueSummary(
    int TotalAmountCents,
    int TotalPlatformFeeCents,
    int CurrentMonthAmountCents,
    int CurrentMonthPlatformFeeCents,
    int ActiveInstanceCount,
    List<InstanceRevenueLine> TopInstances
);

public sealed record InstanceRevenueLine(
    string InstanceId,
    string Domain,
    string DisplayName,
    int AmountCents,
    int PlatformFeeCents
);

public sealed class GetPlatformRevenueHandler(
    HubDbContext dbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPlatformRevenueQuery, Result<PlatformRevenueSummary>>
{
    public async Task<Result<PlatformRevenueSummary>> Handle(
        GetPlatformRevenueQuery request, CancellationToken cancellationToken)
    {
        var userIdResult = currentUserService.GetCurrentUserId();
        if (userIdResult.IsFailure) return userIdResult.Error!;

        // TODO: Add admin check — for now, any authenticated user can view platform revenue
        // In production, this would check for an admin role

        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var allTime = await dbContext.PlatformRevenues
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalAmount = g.Sum(r => r.AmountCents),
                TotalPlatformFee = g.Sum(r => r.PlatformFeeCents)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var currentMonth = await dbContext.PlatformRevenues
            .Where(r => r.CreatedAt >= monthStart)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalAmount = g.Sum(r => r.AmountCents),
                TotalPlatformFee = g.Sum(r => r.PlatformFeeCents)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var activeInstanceCount = await dbContext.InstanceRevenueConfigs
            .CountAsync(c => c.StripeConnectedAccountId != null, cancellationToken);

        var topInstances = await dbContext.PlatformRevenues
            .Where(r => r.CreatedAt >= monthStart)
            .GroupBy(r => r.ManagedInstanceId)
            .Select(g => new
            {
                InstanceId = g.Key,
                AmountCents = g.Sum(r => r.AmountCents),
                PlatformFeeCents = g.Sum(r => r.PlatformFeeCents)
            })
            .OrderByDescending(g => g.AmountCents)
            .Take(10)
            .ToListAsync(cancellationToken);

        var instanceIds = topInstances.Select(t => t.InstanceId).ToList();
        var instances = await dbContext.ManagedInstances
            .AsNoTracking()
            .Where(i => instanceIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        var lines = topInstances.Select(t => new InstanceRevenueLine(
            InstanceId: t.InstanceId.ToString(),
            Domain: instances.TryGetValue(t.InstanceId, out var inst) ? inst.Domain : "unknown",
            DisplayName: instances.TryGetValue(t.InstanceId, out var inst2) ? inst2.DisplayName : "unknown",
            AmountCents: t.AmountCents,
            PlatformFeeCents: t.PlatformFeeCents
        )).ToList();

        return new PlatformRevenueSummary(
            TotalAmountCents: allTime?.TotalAmount ?? 0,
            TotalPlatformFeeCents: allTime?.TotalPlatformFee ?? 0,
            CurrentMonthAmountCents: currentMonth?.TotalAmount ?? 0,
            CurrentMonthPlatformFeeCents: currentMonth?.TotalPlatformFee ?? 0,
            ActiveInstanceCount: activeInstanceCount,
            TopInstances: lines
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/hub/admin/revenue", async (
            GetPlatformRevenueHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetPlatformRevenueQuery(), ct);
        })
        .RequireAuthorization(Policies.User)
        .Produces<PlatformRevenueSummary>(200)
        .WithName("GetPlatformRevenue")
        .WithTags("Billing");
    }
}
