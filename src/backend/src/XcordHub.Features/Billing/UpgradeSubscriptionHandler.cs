using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using XcordHub.Entities;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Billing;

public sealed record ChangePlanCommand(
    long InstanceId,
    FeatureTier TargetFeatureTier,
    UserCountTier TargetUserCountTier,
    bool HdUpgrade = false
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
    IOptions<StripeOptions> stripeOptions,
    IStripeService stripeService,
    IEncryptionService encryptionService,
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

        if (request.HdUpgrade && request.TargetFeatureTier != FeatureTier.Video)
            return Error.Validation("VALIDATION_FAILED", "HD upgrade requires Video feature tier");

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
            instance.Billing.UserCountTier == request.TargetUserCountTier &&
            instance.Billing.HdUpgrade == request.HdUpgrade)
            return Error.BadRequest("SAME_PLAN", "Instance is already on this plan");

        var priceCents = TierDefaults.GetPriceCents(request.TargetFeatureTier, request.TargetUserCountTier, request.HdUpgrade);

        // If Stripe is configured and this is a paid plan, create a checkout session
        var options = stripeOptions.Value;
        if (options.IsConfigured && priceCents > 0)
        {
            // Ensure Stripe customer exists for this user
            var user = await dbContext.HubUsers.FindAsync([userId], cancellationToken);
            if (user == null)
                return Error.NotFound("USER_NOT_FOUND", "User not found");

            var email = encryptionService.Decrypt(user.Email);

            if (string.IsNullOrWhiteSpace(user.StripeCustomerId))
            {
                user.StripeCustomerId = await stripeService.EnsureCustomerAsync(
                    userId, email, user.DisplayName, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            // Build a Stripe Price ID from the plan combination
            var priceId = BuildStripePriceId(request.TargetFeatureTier, request.TargetUserCountTier, request.HdUpgrade);

            var baseUrl = configuration.GetValue<string>("Hub:BaseUrl") ?? "https://xcord-dev.net";
            var checkout = await stripeService.CreateCheckoutSessionAsync(new CreateCheckoutRequest(
                CustomerId: user.StripeCustomerId,
                PriceId: priceId,
                InstanceId: instance.Id,
                SuccessUrl: $"{baseUrl}/dashboard/billing?checkout=success&instance={instance.Id}",
                CancelUrl: $"{baseUrl}/dashboard/billing?checkout=cancelled"
            ), cancellationToken);

            return new ChangePlanResponse(
                FeatureTier: request.TargetFeatureTier.ToString(),
                UserCountTier: request.TargetUserCountTier.ToString(),
                PriceCents: priceCents,
                CheckoutUrl: checkout.CheckoutUrl,
                RequiresCheckout: true
            );
        }

        // No Stripe configured (dev/self-hosted): apply plan change directly
        instance.Billing.FeatureTier = request.TargetFeatureTier;
        instance.Billing.UserCountTier = request.TargetUserCountTier;
        instance.Billing.HdUpgrade = request.HdUpgrade;

        // Update config with new resource limits + feature flags
        if (instance.Config != null)
        {
            instance.Config.ResourceLimitsJson = JsonSerializer.Serialize(
                TierDefaults.GetResourceLimits(request.TargetUserCountTier));
            instance.Config.FeatureFlagsJson = JsonSerializer.Serialize(
                TierDefaults.GetFeatureFlags(request.TargetFeatureTier, request.HdUpgrade));
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

    private static string BuildStripePriceId(FeatureTier feature, UserCountTier users, bool hdUpgrade)
    {
        // Convention: price_xcord_{feature}_{users}[_hd]
        // e.g. price_xcord_video_50_hd, price_xcord_chat_100
        var suffix = hdUpgrade ? "_hd" : "";
        return $"price_xcord_{feature.ToString().ToLowerInvariant()}_{(int)users}{suffix}";
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
