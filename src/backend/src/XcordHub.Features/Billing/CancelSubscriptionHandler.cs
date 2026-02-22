using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Billing;

public sealed record CancelSubscriptionCommand();

public sealed record CancelSubscriptionResponse(
    string Message,
    string NewTier
);

public sealed class CancelSubscriptionHandler(
    HubDbContext dbContext,
    ICurrentUserService currentUserService,
    ILogger<CancelSubscriptionHandler> logger)
    : IRequestHandler<CancelSubscriptionCommand, Result<CancelSubscriptionResponse>>
{
    public async Task<Result<CancelSubscriptionResponse>> Handle(
        CancelSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var userIdResult = currentUserService.GetCurrentUserId();
        if (userIdResult.IsFailure) return userIdResult.Error!;
        var userId = userIdResult.Value;

        var user = await dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, cancellationToken);

        if (user == null)
            return Error.NotFound("USER_NOT_FOUND", "User not found");

        if (user.SubscriptionTier == BillingTier.Free)
            return Error.BadRequest("ALREADY_FREE", "You are already on the Free plan");

        logger.LogInformation(
            "User {UserId} cancelling subscription (tier: {Tier}, stripeSubId: {SubId})",
            userId, user.SubscriptionTier, user.StripeSubscriptionId ?? "none");

        // TODO: Cancel via Stripe API when billing is wired up. Downgrades to Free directly.
        var previousTier = user.SubscriptionTier;
        user.SubscriptionTier = BillingTier.Free;
        user.StripeSubscriptionId = null;

        // Downgrade all instance billing records owned by this user to Free tier
        var instances = await dbContext.ManagedInstances
            .Include(i => i.Billing)
            .Where(i => i.OwnerId == userId && i.DeletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var instance in instances)
        {
            if (instance.Billing != null)
            {
                instance.Billing.Tier = BillingTier.Free;
                instance.Billing.BillingStatus = BillingStatus.Cancelled;
                instance.Billing.StripeSubscriptionId = null;
                instance.Billing.CurrentPeriodEnd = null;
                instance.Billing.NextBillingDate = null;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "User {UserId} subscription cancelled, downgraded from {PreviousTier} to Free",
            userId, previousTier);

        return new CancelSubscriptionResponse(
            Message: "Your subscription has been cancelled. You have been moved to the Free plan.",
            NewTier: "Free"
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/hub/billing/cancel", async (
            CancelSubscriptionHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new CancelSubscriptionCommand(), ct);
        })
        .RequireAuthorization(Policies.User)
        .Produces<CancelSubscriptionResponse>(200)
        .WithName("CancelSubscription")
        .WithTags("Billing");
    }
}
