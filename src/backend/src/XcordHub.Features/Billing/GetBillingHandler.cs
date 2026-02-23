using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Billing;

public sealed record GetBillingQuery();

public sealed record InstanceBillingItem(
    string InstanceId,
    string Domain,
    string DisplayName,
    string FeatureTier,
    string UserCountTier,
    int PriceCents,
    string BillingStatus
);

public sealed record GetBillingResponse(
    List<InstanceBillingItem> Instances
);

public sealed class GetBillingHandler(HubDbContext dbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetBillingQuery, Result<GetBillingResponse>>
{
    public async Task<Result<GetBillingResponse>> Handle(GetBillingQuery request, CancellationToken cancellationToken)
    {
        var userIdResult = currentUserService.GetCurrentUserId();
        if (userIdResult.IsFailure) return userIdResult.Error!;
        var userId = userIdResult.Value;

        var userExists = await dbContext.HubUsers
            .AnyAsync(u => u.Id == userId && u.DeletedAt == null, cancellationToken);

        if (!userExists)
            return Error.NotFound("USER_NOT_FOUND", "User not found");

        var instances = await dbContext.ManagedInstances
            .Include(i => i.Billing)
            .Where(i => i.OwnerId == userId && i.DeletedAt == null)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new
            {
                i.Id,
                i.Domain,
                i.DisplayName,
                i.Billing!.FeatureTier,
                i.Billing.UserCountTier,
                i.Billing.BillingStatus
            })
            .ToListAsync(cancellationToken);

        var items = instances.Select(i => new InstanceBillingItem(
            i.Id.ToString(),
            i.Domain,
            i.DisplayName,
            i.FeatureTier.ToString(),
            i.UserCountTier.ToString(),
            TierDefaults.GetPriceCents(i.FeatureTier, i.UserCountTier),
            i.BillingStatus.ToString()
        )).ToList();

        return new GetBillingResponse(items);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/hub/billing", async (
            GetBillingHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetBillingQuery(), ct);
        })
        .RequireAuthorization(Policies.User)
        .Produces<GetBillingResponse>(200)
        .WithName("GetBilling")
        .WithTags("Billing");
    }
}
