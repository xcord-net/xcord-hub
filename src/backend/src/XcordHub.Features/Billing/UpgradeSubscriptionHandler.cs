using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Billing;

public sealed record UpgradeSubscriptionCommand(string TargetTier);

public sealed record UpgradeSubscriptionResponse(
    string Tier,
    string? CheckoutUrl,
    bool RequiresCheckout
);

public sealed class UpgradeSubscriptionHandler(
    HubDbContext dbContext,
    ICurrentUserService currentUserService,
    IConfiguration configuration)
    : IRequestHandler<UpgradeSubscriptionCommand, Result<UpgradeSubscriptionResponse>>,
      IValidatable<UpgradeSubscriptionCommand>
{
    public Error? Validate(UpgradeSubscriptionCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.TargetTier))
            return Error.Validation("VALIDATION_FAILED", "TargetTier is required");

        if (!Enum.TryParse<BillingTier>(request.TargetTier, ignoreCase: true, out _))
            return Error.Validation("VALIDATION_FAILED", $"Invalid tier '{request.TargetTier}'. Valid values: Free, Basic, Pro");

        return null;
    }

    public async Task<Result<UpgradeSubscriptionResponse>> Handle(
        UpgradeSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var userIdResult = currentUserService.GetCurrentUserId();
        if (userIdResult.IsFailure) return userIdResult.Error!;
        var userId = userIdResult.Value;

        var user = await dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, cancellationToken);

        if (user == null)
            return Error.NotFound("USER_NOT_FOUND", "User not found");

        var targetTier = Enum.Parse<BillingTier>(request.TargetTier, ignoreCase: true);

        if (targetTier == user.SubscriptionTier)
            return Error.BadRequest("SAME_TIER", "You are already on this plan");

        // Check if Stripe is configured
        var stripeKey = configuration.GetValue<string>("Stripe:SecretKey");
        if (!string.IsNullOrWhiteSpace(stripeKey) && targetTier != BillingTier.Free)
        {
            // TODO: Create Stripe checkout session when billing is wired up.
            // Returns a placeholder checkout URL.
            var baseUrl = configuration.GetValue<string>("Hub:BaseUrl") ?? "https://xcord-dev.net";
            var checkoutUrl = $"{baseUrl}/dashboard/billing?checkout=pending";

            return new UpgradeSubscriptionResponse(
                Tier: targetTier.ToString(),
                CheckoutUrl: checkoutUrl,
                RequiresCheckout: true
            );
        }

        // No Stripe configured (dev/self-hosted): apply tier change directly
        user.SubscriptionTier = targetTier;

        // If downgrading to free, clear Stripe info
        if (targetTier == BillingTier.Free)
        {
            user.StripeCustomerId = null;
            user.StripeSubscriptionId = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpgradeSubscriptionResponse(
            Tier: targetTier.ToString(),
            CheckoutUrl: null,
            RequiresCheckout: false
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/hub/billing/upgrade", async (
            UpgradeSubscriptionCommand command,
            UpgradeSubscriptionHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(command, ct);
        })
        .RequireAuthorization(Policies.User)
        .Produces<UpgradeSubscriptionResponse>(200)
        .WithName("UpgradeSubscription")
        .WithTags("Billing");
    }
}
