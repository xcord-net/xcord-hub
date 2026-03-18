using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using XcordHub.Infrastructure.Options;

namespace XcordHub.Features.Config;

public sealed record GetFeaturesResponse(bool PaymentsEnabled);

public sealed class GetFeaturesHandler : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/hub/features", (IOptions<StripeOptions> stripeOptions) =>
        {
            return Results.Ok(new GetFeaturesResponse(
                PaymentsEnabled: stripeOptions.Value.IsConfigured));
        })
        .AllowAnonymous()
        .Produces<GetFeaturesResponse>(200)
        .WithName("GetFeatures")
        .WithTags("Config");
    }
}
