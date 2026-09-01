using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Config;

public sealed record GetFeaturesResponse(
    bool PaymentsEnabled,
    string? StripePublishableKey,
    bool PaidServersDisabled,
    bool DevLoginEnabled);

public sealed class GetFeaturesHandler : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/hub/features", async (
            IOptions<StripeOptions> stripeOptions,
            ISystemConfigService systemConfigService,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            var opts = stripeOptions.Value;
            var systemConfig = await systemConfigService.GetAsync(ct).ConfigureAwait(false);
            // Mirrors the gate on TestSeedEndpoint in Program.cs, so the login
            // page only offers the dev login button where the route exists.
            var devLoginEnabled = !string.IsNullOrEmpty(configuration["TestSeed:Key"]);
            return Results.Ok(new GetFeaturesResponse(
                PaymentsEnabled: opts.IsConfigured,
                StripePublishableKey: opts.IsConfigured ? opts.PublishableKey : null,
                PaidServersDisabled: systemConfig.PaidServersDisabled,
                DevLoginEnabled: devLoginEnabled));
        })
        .AllowAnonymous()
        .Produces<GetFeaturesResponse>(200)
        .WithName("GetFeatures")
        .WithTags("Config");
    }
}
