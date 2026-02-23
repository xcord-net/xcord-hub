using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Billing;

public sealed record CancelInstanceBillingCommand(long InstanceId);

public sealed record CancelInstanceBillingResponse(
    string Message,
    string FeatureTier,
    string UserCountTier
);

public sealed class CancelInstanceBillingHandler(
    HubDbContext dbContext,
    ICurrentUserService currentUserService,
    ILogger<CancelInstanceBillingHandler> logger)
    : IRequestHandler<CancelInstanceBillingCommand, Result<CancelInstanceBillingResponse>>
{
    public async Task<Result<CancelInstanceBillingResponse>> Handle(
        CancelInstanceBillingCommand request, CancellationToken cancellationToken)
    {
        var userIdResult = currentUserService.GetCurrentUserId();
        if (userIdResult.IsFailure) return userIdResult.Error!;
        var userId = userIdResult.Value;

        var instance = await dbContext.ManagedInstances
            .Include(i => i.Billing)
            .Include(i => i.Config)
            .FirstOrDefaultAsync(i => i.Id == request.InstanceId && i.DeletedAt == null, cancellationToken);

        if (instance == null)
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");

        if (instance.OwnerId != userId)
            return Error.Forbidden("NOT_OWNER", "You do not own this instance");

        if (instance.Billing == null)
            return Error.NotFound("BILLING_NOT_FOUND", "Billing record not found for this instance");

        if (instance.Billing.FeatureTier == FeatureTier.Chat &&
            instance.Billing.UserCountTier == UserCountTier.Tier10)
            return Error.BadRequest("ALREADY_FREE", "This instance is already on the free plan");

        logger.LogInformation(
            "User {UserId} cancelling billing for instance {InstanceId} (feature: {FeatureTier}, users: {UserCountTier})",
            userId, request.InstanceId, instance.Billing.FeatureTier, instance.Billing.UserCountTier);

        // TODO: Cancel via Stripe API when billing is wired up.
        instance.Billing.FeatureTier = FeatureTier.Chat;
        instance.Billing.UserCountTier = UserCountTier.Tier10;
        instance.Billing.BillingStatus = BillingStatus.Cancelled;
        instance.Billing.StripeSubscriptionId = null;
        instance.Billing.CurrentPeriodEnd = null;
        instance.Billing.NextBillingDate = null;

        // Update config with free tier defaults
        if (instance.Config != null)
        {
            instance.Config.ResourceLimitsJson = JsonSerializer.Serialize(
                TierDefaults.GetResourceLimits(UserCountTier.Tier10));
            instance.Config.FeatureFlagsJson = JsonSerializer.Serialize(
                TierDefaults.GetFeatureFlags(FeatureTier.Chat));
            instance.Config.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Instance {InstanceId} billing cancelled, downgraded to Chat + Tier10",
            request.InstanceId);

        return new CancelInstanceBillingResponse(
            Message: "Instance billing has been cancelled. The instance has been moved to the free plan.",
            FeatureTier: FeatureTier.Chat.ToString(),
            UserCountTier: UserCountTier.Tier10.ToString()
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/hub/instances/{instanceId}/billing/cancel", async (
            long instanceId,
            CancelInstanceBillingHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new CancelInstanceBillingCommand(instanceId), ct);
        })
        .RequireAuthorization(Policies.User)
        .Produces<CancelInstanceBillingResponse>(200)
        .WithName("CancelInstanceBilling")
        .WithTags("Billing");
    }
}
