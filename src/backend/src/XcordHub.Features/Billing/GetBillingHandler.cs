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

public sealed record BillingTierFeature(string Name, string Value);

public sealed record BillingTierInfo(
    string Name,
    string Price,
    string Period,
    int MaxInstances,
    int MaxUsersPerInstance,
    int MaxStorageMb,
    List<BillingTierFeature> Features
);

public sealed record GetBillingResponse(
    string Tier,
    string Status,
    bool HasStripeSubscription,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? NextBillingDate,
    int InstanceCount,
    int MaxInstances,
    BillingTierInfo CurrentTierInfo,
    List<BillingTierInfo> AvailableTiers
);

public sealed class GetBillingHandler(HubDbContext dbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetBillingQuery, Result<GetBillingResponse>>
{
    public async Task<Result<GetBillingResponse>> Handle(GetBillingQuery request, CancellationToken cancellationToken)
    {
        var userIdResult = currentUserService.GetCurrentUserId();
        if (userIdResult.IsFailure) return userIdResult.Error!;
        var userId = userIdResult.Value;

        var user = await dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, cancellationToken);

        if (user == null)
            return Error.NotFound("USER_NOT_FOUND", "User not found");

        var instanceCount = await dbContext.ManagedInstances
            .CountAsync(i => i.OwnerId == userId && i.DeletedAt == null, cancellationToken);

        var maxInstances = TierDefaults.GetMaxInstancesForTier(user.SubscriptionTier);

        // Determine billing status from Stripe subscription presence
        var status = user.StripeSubscriptionId != null ? "Active" : "Free";

        var currentTierInfo = BuildTierInfo(user.SubscriptionTier);
        var availableTiers = new List<BillingTierInfo>
        {
            BuildTierInfo(BillingTier.Free),
            BuildTierInfo(BillingTier.Basic),
            BuildTierInfo(BillingTier.Pro),
        };

        return new GetBillingResponse(
            Tier: user.SubscriptionTier.ToString(),
            Status: status,
            HasStripeSubscription: user.StripeSubscriptionId != null,
            CurrentPeriodEnd: null, // TODO: populate from Stripe subscription
            NextBillingDate: null,  // TODO: populate from Stripe subscription
            InstanceCount: instanceCount,
            MaxInstances: maxInstances,
            CurrentTierInfo: currentTierInfo,
            AvailableTiers: availableTiers
        );
    }

    private static BillingTierInfo BuildTierInfo(BillingTier tier)
    {
        return tier switch
        {
            BillingTier.Free => new BillingTierInfo(
                Name: "Free",
                Price: "$0",
                Period: "/month",
                MaxInstances: TierDefaults.GetMaxInstancesForTier(BillingTier.Free),
                MaxUsersPerInstance: TierDefaults.GetResourceLimits(BillingTier.Free).MaxUsers,
                MaxStorageMb: TierDefaults.GetResourceLimits(BillingTier.Free).MaxStorageMb,
                Features: new List<BillingTierFeature>
                {
                    new("Instances", "1"),
                    new("Members", "50"),
                    new("Storage", "1 GB"),
                    new("Voice channels", "Yes"),
                    new("Support", "Community"),
                }
            ),
            BillingTier.Basic => new BillingTierInfo(
                Name: "Basic",
                Price: "TBD",
                Period: "/month",
                MaxInstances: TierDefaults.GetMaxInstancesForTier(BillingTier.Basic),
                MaxUsersPerInstance: TierDefaults.GetResourceLimits(BillingTier.Basic).MaxUsers,
                MaxStorageMb: TierDefaults.GetResourceLimits(BillingTier.Basic).MaxStorageMb,
                Features: new List<BillingTierFeature>
                {
                    new("Instances", "1"),
                    new("Members", "250"),
                    new("Storage", "10 GB"),
                    new("Video & screen sharing", "Yes"),
                    new("Support", "Email"),
                }
            ),
            BillingTier.Pro => new BillingTierInfo(
                Name: "Pro",
                Price: "TBD",
                Period: "/month",
                MaxInstances: TierDefaults.GetMaxInstancesForTier(BillingTier.Pro),
                MaxUsersPerInstance: TierDefaults.GetResourceLimits(BillingTier.Pro).MaxUsers,
                MaxStorageMb: TierDefaults.GetResourceLimits(BillingTier.Pro).MaxStorageMb,
                Features: new List<BillingTierFeature>
                {
                    new("Instances", "1"),
                    new("Members", "1,000"),
                    new("Storage", "50 GB"),
                    new("Video, screen sharing & Go Live", "Yes"),
                    new("Support", "Priority"),
                }
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown billing tier")
        };
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
