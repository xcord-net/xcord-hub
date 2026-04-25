using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Config;

public sealed record GetFeaturesResponse(
    bool PaymentsEnabled,
    string? StripePublishableKey,
    bool PaidServersDisabled);

public sealed class GetFeaturesHandler : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/hub/features", async (
            IOptions<StripeOptions> stripeOptions,
            ISystemConfigService systemConfigService,
            CancellationToken ct) =>
        {
            var opts = stripeOptions.Value;
            var systemConfig = await systemConfigService.GetAsync(ct);
            return Results.Ok(new GetFeaturesResponse(
                PaymentsEnabled: opts.IsConfigured,
                StripePublishableKey: opts.IsConfigured ? opts.PublishableKey : null,
                PaidServersDisabled: systemConfig.PaidServersDisabled));
        })
        .AllowAnonymous()
        .Produces<GetFeaturesResponse>(200)
        .WithName("GetFeatures")
        .WithTags("Config");
    }
}
