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
            BuildTierInfo(BillingTier.Pro),
            BuildTierInfo(BillingTier.Enterprise),
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
                    new("Members per instance", "100"),
                    new("Storage", "1 GB"),
                    new("Voice channels", "Included"),
                    new("Support", "Community"),
                }
            ),
            BillingTier.Pro => new BillingTierInfo(
                Name: "Pro",
                Price: "$15",
                Period: "/month",
                MaxInstances: TierDefaults.GetMaxInstancesForTier(BillingTier.Pro),
                MaxUsersPerInstance: TierDefaults.GetResourceLimits(BillingTier.Pro).MaxUsers,
                MaxStorageMb: TierDefaults.GetResourceLimits(BillingTier.Pro).MaxStorageMb,
                Features: new List<BillingTierFeature>
                {
                    new("Instances", "10"),
                    new("Members per instance", "10,000"),
                    new("Storage", "10 GB"),
                    new("Voice & video channels", "Included"),
                    new("Custom emoji", "Included"),
                    new("Webhooks & bots", "Included"),
                    new("Forum channels", "Included"),
                    new("Scheduled events", "Included"),
                    new("Support", "Email"),
                }
            ),
            BillingTier.Enterprise => new BillingTierInfo(
                Name: "Enterprise",
                Price: "Custom",
                Period: "",
                MaxInstances: -1,
                MaxUsersPerInstance: -1,
                MaxStorageMb: -1,
                Features: new List<BillingTierFeature>
                {
                    new("Instances", "Unlimited"),
                    new("Members per instance", "Unlimited"),
                    new("Storage", "Unlimited"),
                    new("All Pro features", "Included"),
                    new("SLA", "99.9% uptime"),
                    new("Support", "Dedicated"),
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
        .WithName("GetBilling")
        .WithTags("Billing");
    }
}
