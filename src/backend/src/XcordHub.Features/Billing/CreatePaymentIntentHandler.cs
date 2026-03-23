using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using XcordHub.Entities;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Billing;

public sealed record CreateSetupIntentCommand(
    string Tier,
    bool MediaEnabled
);

public sealed record CreateSetupIntentResponse(
    string ClientSecret,
    int PriceCents
);

public sealed class CreateSetupIntentHandler(
    IStripeService stripeService,
    IOptions<StripeOptions> stripeOptions)
    : IRequestHandler<CreateSetupIntentCommand, Result<CreateSetupIntentResponse>>
{
    public async Task<Result<CreateSetupIntentResponse>> Handle(
        CreateSetupIntentCommand request, CancellationToken cancellationToken)
    {
        if (!stripeOptions.Value.IsConfigured)
            return Error.Validation("STRIPE_NOT_CONFIGURED", "Payment processing is not configured");

        if (!Enum.TryParse<InstanceTier>(request.Tier, ignoreCase: true, out var tier) || !Enum.IsDefined(tier))
            return Error.Validation("VALIDATION_FAILED", "Invalid tier");

        var requiresPayment = tier != InstanceTier.Free || request.MediaEnabled;
        if (!requiresPayment)
            return Error.Validation("FREE_TIER_NO_PAYMENT", "Free tier without media does not require payment");

        var priceCents = TierDefaults.GetTotalPriceCents(tier, request.MediaEnabled);

        var metadata = new Dictionary<string, string>
        {
            ["tier"] = request.Tier,
            ["mediaEnabled"] = request.MediaEnabled.ToString().ToLowerInvariant()
        };

        var result = await stripeService.CreateSetupIntentAsync(metadata, cancellationToken);

        return new CreateSetupIntentResponse(result.ClientSecret, priceCents);
    }

    // Keep the same route path for backward compatibility with frontend
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/hub/billing/create-payment-intent", async (
            CreateSetupIntentCommand command,
            CreateSetupIntentHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(command, ct);
        })
        .AllowAnonymous()
        .Produces<CreateSetupIntentResponse>(200)
        .WithName("CreateSetupIntent")
        .WithTags("Billing");
    }
}
