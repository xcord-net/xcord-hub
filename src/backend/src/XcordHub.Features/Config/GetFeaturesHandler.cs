using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using XcordHub.Infrastructure.Options;

namespace XcordHub.Features.Config;

public sealed record GetFeaturesResponse(bool PaymentsEnabled, string? StripePublishableKey);

public sealed class GetFeaturesHandler : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/hub/features", (IOptions<StripeOptions> stripeOptions) =>
        {
            var opts = stripeOptions.Value;
            return Results.Ok(new GetFeaturesResponse(
                PaymentsEnabled: opts.IsConfigured,
                StripePublishableKey: opts.IsConfigured ? opts.PublishableKey : null));
        })
        .AllowAnonymous()
        .Produces<GetFeaturesResponse>(200)
        .WithName("GetFeatures")
        .WithTags("Config");
    }
}
