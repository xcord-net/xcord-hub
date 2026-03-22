using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Instances;

public sealed record CheckSubdomainQuery(string Subdomain);

public sealed record CheckSubdomainResponse(
    bool Available,
    string? Reason
);

public sealed class CheckSubdomainHandler(
    HubDbContext dbContext,
    IConfiguration configuration)
    : IRequestHandler<CheckSubdomainQuery, Result<CheckSubdomainResponse>>,
      IValidatable<CheckSubdomainQuery>
{
    public Error? Validate(CheckSubdomainQuery request)
    {
        if (string.IsNullOrWhiteSpace(request.Subdomain))
            return Error.Validation("VALIDATION_FAILED", "Subdomain is required");

        return null;
    }

    public async Task<Result<CheckSubdomainResponse>> Handle(
        CheckSubdomainQuery request, CancellationToken cancellationToken)
    {
        // Run validation rules (length, format, reserved words)
        var validationError = ValidationHelpers.ValidateSubdomain(request.Subdomain);
        if (validationError != null)
        {
            return new CheckSubdomainResponse(
                Available: false,
                Reason: validationError.Message
            );
        }

        // Check database for existing domain
        var baseDomain = configuration.GetValue<string>("Hub:BaseDomain") ?? "xcord-dev.net";
        var domain = $"{request.Subdomain}.{baseDomain}";

        var domainExists = await dbContext.ManagedInstances
            .AnyAsync(i => i.Domain == domain && i.DeletedAt == null, cancellationToken);

        if (domainExists)
        {
            return new CheckSubdomainResponse(
                Available: false,
                Reason: "This subdomain is already taken"
            );
        }

        return new CheckSubdomainResponse(
            Available: true,
            Reason: null
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/hub/check-subdomain", async (
            string subdomain,
            CheckSubdomainHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new CheckSubdomainQuery(subdomain), ct);
        })
        .AllowAnonymous()
        .Produces<CheckSubdomainResponse>(200)
        .WithName("CheckSubdomain")
        .WithTags("Instances");
    }
}
