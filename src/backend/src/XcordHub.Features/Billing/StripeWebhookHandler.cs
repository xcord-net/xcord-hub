using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using XcordHub.Entities;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;

namespace XcordHub.Features.Billing;

public sealed class StripeWebhookHandler(
    HubDbContext dbContext,
    IOptions<StripeOptions> stripeOptions,
    ILogger<StripeWebhookHandler> logger)
{
    public async Task<IResult> HandleAsync(HttpContext httpContext, CancellationToken ct)
    {
        var options = stripeOptions.Value;
        if (!options.IsConfigured)
        {
            return Results.StatusCode(503);
        }

        var json = await new StreamReader(httpContext.Request.Body).ReadToEndAsync(ct);
        Event stripeEvent;

        try
        {
            stripeEvent = !string.IsNullOrWhiteSpace(options.WebhookSecret)
                ? EventUtility.ConstructEvent(json, httpContext.Request.Headers["Stripe-Signature"], options.WebhookSecret)
                : EventUtility.ParseEvent(json);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe webhook signature verification failed");
            return Results.BadRequest("Invalid signature");
        }

        logger.LogInformation("Processing Stripe webhook event {EventType} ({EventId})", stripeEvent.Type, stripeEvent.Id);

        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
                await HandleCheckoutCompleted(stripeEvent, ct);
                break;

            case EventTypes.InvoicePaid:
                await HandleInvoicePaid(stripeEvent, ct);
                break;

            case EventTypes.InvoicePaymentFailed:
                await HandlePaymentFailed(stripeEvent, ct);
                break;

            case EventTypes.CustomerSubscriptionUpdated:
                await HandleSubscriptionUpdated(stripeEvent, ct);
                break;

            case EventTypes.CustomerSubscriptionDeleted:
                await HandleSubscriptionDeleted(stripeEvent, ct);
                break;

            default:
                logger.LogDebug("Unhandled Stripe event type: {EventType}", stripeEvent.Type);
                break;
        }

        return Results.Ok();
    }

    private async Task HandleCheckoutCompleted(Event stripeEvent, CancellationToken ct)
    {
        var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
        if (session == null) return;

        if (!session.Metadata.TryGetValue("instance_id", out var instanceIdStr) ||
            !long.TryParse(instanceIdStr, out var instanceId))
        {
            logger.LogWarning("Checkout session {SessionId} missing instance_id metadata", session.Id);
            return;
        }

        var billing = await dbContext.InstanceBillings
            .FirstOrDefaultAsync(b => b.ManagedInstanceId == instanceId, ct);

        if (billing == null)
        {
            logger.LogWarning("No billing record for instance {InstanceId}", instanceId);
            return;
        }

        billing.StripeSubscriptionId = session.SubscriptionId;
        billing.BillingStatus = BillingStatus.Active;

        await dbContext.SaveChangesAsync(ct);
        logger.LogInformation("Checkout completed for instance {InstanceId}, subscription {SubscriptionId}",
            instanceId, session.SubscriptionId);
    }

    private async Task HandleInvoicePaid(Event stripeEvent, CancellationToken ct)
    {
        var invoice = stripeEvent.Data.Object as Stripe.Invoice;
        var subscriptionId = invoice?.Parent?.SubscriptionDetails?.SubscriptionId;
        if (subscriptionId == null) return;

        var billing = await dbContext.InstanceBillings
            .FirstOrDefaultAsync(b => b.StripeSubscriptionId == subscriptionId, ct);

        if (billing == null) return;

        billing.BillingStatus = BillingStatus.Active;
        billing.CurrentPeriodEnd = invoice!.PeriodEnd;
        billing.NextBillingDate = invoice.PeriodEnd;

        await dbContext.SaveChangesAsync(ct);
        logger.LogInformation("Invoice paid for subscription {SubscriptionId}", subscriptionId);
    }

    private async Task HandlePaymentFailed(Event stripeEvent, CancellationToken ct)
    {
        var invoice = stripeEvent.Data.Object as Stripe.Invoice;
        var subscriptionId = invoice?.Parent?.SubscriptionDetails?.SubscriptionId;
        if (subscriptionId == null) return;

        var billing = await dbContext.InstanceBillings
            .FirstOrDefaultAsync(b => b.StripeSubscriptionId == subscriptionId, ct);

        if (billing == null) return;

        billing.BillingStatus = BillingStatus.PastDue;

        await dbContext.SaveChangesAsync(ct);
        logger.LogWarning("Payment failed for subscription {SubscriptionId}, status set to PastDue",
            subscriptionId);
    }

    private async Task HandleSubscriptionUpdated(Event stripeEvent, CancellationToken ct)
    {
        var subscription = stripeEvent.Data.Object as Stripe.Subscription;
        if (subscription == null) return;

        var billing = await dbContext.InstanceBillings
            .FirstOrDefaultAsync(b => b.StripeSubscriptionId == subscription.Id, ct);

        if (billing == null) return;

        if (subscription.Items?.Data?.Count > 0)
        {
            billing.StripePriceId = subscription.Items.Data[0].Price.Id;
        }

        await dbContext.SaveChangesAsync(ct);
        logger.LogInformation("Subscription {SubscriptionId} updated", subscription.Id);
    }

    private async Task HandleSubscriptionDeleted(Event stripeEvent, CancellationToken ct)
    {
        var subscription = stripeEvent.Data.Object as Stripe.Subscription;
        if (subscription == null) return;

        var billing = await dbContext.InstanceBillings
            .Include(b => b.ManagedInstance)
                .ThenInclude(i => i.Config)
            .FirstOrDefaultAsync(b => b.StripeSubscriptionId == subscription.Id, ct);

        if (billing == null) return;

        // Downgrade to free tier
        billing.FeatureTier = FeatureTier.Chat;
        billing.UserCountTier = UserCountTier.Tier10;
        billing.HdUpgrade = false;
        billing.BillingStatus = BillingStatus.Cancelled;
        billing.StripeSubscriptionId = null;
        billing.StripePriceId = null;
        billing.CurrentPeriodEnd = null;
        billing.NextBillingDate = null;

        if (billing.ManagedInstance?.Config != null)
        {
            billing.ManagedInstance.Config.ResourceLimitsJson = JsonSerializer.Serialize(
                TierDefaults.GetResourceLimits(UserCountTier.Tier10));
            billing.ManagedInstance.Config.FeatureFlagsJson = JsonSerializer.Serialize(
                TierDefaults.GetFeatureFlags(FeatureTier.Chat));
            billing.ManagedInstance.Config.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(ct);
        logger.LogInformation("Subscription {SubscriptionId} deleted, instance downgraded to free tier",
            subscription.Id);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/hub/billing/stripe-webhook", async (
            StripeWebhookHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            return await handler.HandleAsync(httpContext, ct);
        })
        .WithName("StripeWebhook")
        .WithTags("Billing");
    }
}
