using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using XcordHub.Entities;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Billing;

public sealed record ChangePlanCommand(
    long InstanceId,
    FeatureTier TargetFeatureTier,
    UserCountTier TargetUserCountTier
);

public sealed record ChangePlanResponse(
    string FeatureTier,
    string UserCountTier,
    int PriceCents,
    string? CheckoutUrl,
    bool RequiresCheckout
);

public sealed class ChangePlanHandler(
    HubDbContext dbContext,
    ICurrentUserService currentUserService,
    IConfiguration configuration)
    : IRequestHandler<ChangePlanCommand, Result<ChangePlanResponse>>,
      IValidatable<ChangePlanCommand>
{
    public Error? Validate(ChangePlanCommand request)
    {
        if (request.InstanceId <= 0)
            return Error.Validation("VALIDATION_FAILED", "InstanceId is required");

        if (!Enum.IsDefined(request.TargetFeatureTier))
            return Error.Validation("VALIDATION_FAILED", "Invalid feature tier");

        if (!Enum.IsDefined(request.TargetUserCountTier))
            return Error.Validation("VALIDATION_FAILED", "Invalid user count tier");

        return null;
    }

    public async Task<Result<ChangePlanResponse>> Handle(
        ChangePlanCommand request, CancellationToken cancellationToken)
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

        if (instance.Billing.FeatureTier == request.TargetFeatureTier &&
            instance.Billing.UserCountTier == request.TargetUserCountTier)
            return Error.BadRequest("SAME_PLAN", "Instance is already on this plan");

        var priceCents = TierDefaults.GetPriceCents(request.TargetFeatureTier, request.TargetUserCountTier);

        // Check if Stripe checkout is needed for paid plans
        var stripeKey = configuration.GetValue<string>("Stripe:SecretKey");
        if (!string.IsNullOrWhiteSpace(stripeKey) && priceCents > 0)
        {
            // TODO: Create Stripe checkout session when billing is wired up.
            var baseUrl = configuration.GetValue<string>("Hub:BaseUrl") ?? "https://xcord-dev.net";
            var checkoutUrl = $"{baseUrl}/dashboard/billing?checkout=pending";

            return new ChangePlanResponse(
                FeatureTier: request.TargetFeatureTier.ToString(),
                UserCountTier: request.TargetUserCountTier.ToString(),
                PriceCents: priceCents,
                CheckoutUrl: checkoutUrl,
                RequiresCheckout: true
            );
        }

        // No Stripe configured (dev/self-hosted): apply plan change directly
        instance.Billing.FeatureTier = request.TargetFeatureTier;
        instance.Billing.UserCountTier = request.TargetUserCountTier;

        // Update config with new resource limits + feature flags
        if (instance.Config != null)
        {
            instance.Config.ResourceLimitsJson = JsonSerializer.Serialize(
                TierDefaults.GetResourceLimits(request.TargetUserCountTier));
            instance.Config.FeatureFlagsJson = JsonSerializer.Serialize(
                TierDefaults.GetFeatureFlags(request.TargetFeatureTier));
            instance.Config.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ChangePlanResponse(
            FeatureTier: request.TargetFeatureTier.ToString(),
            UserCountTier: request.TargetUserCountTier.ToString(),
            PriceCents: priceCents,
            CheckoutUrl: null,
            RequiresCheckout: false
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/hub/instances/{instanceId}/billing/change", async (
            long instanceId,
            ChangePlanCommand command,
            ChangePlanHandler handler,
            CancellationToken ct) =>
        {
            var cmd = command with { InstanceId = instanceId };
            return await handler.ExecuteAsync(cmd, ct);
        })
        .RequireAuthorization(Policies.User)
        .Produces<ChangePlanResponse>(200)
        .WithName("ChangePlan")
        .WithTags("Billing");
    }
}
