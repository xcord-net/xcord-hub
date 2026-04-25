using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using XcordHub.Entities;
using XcordHub.Features.Auth;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Instances;

public sealed record CreateInstanceCommand(
    string Subdomain,
    string DisplayName,
    InstanceTier Tier = InstanceTier.Free,
    bool MediaEnabled = false,
    string? AdminPassword = null,
    string? CaptchaId = null,
    string? CaptchaAnswer = null
);

public sealed record CreateInstanceResponse(
    string InstanceId,
    string Domain,
    string DisplayName,
    string Status,
    string AdminPassword
);

public sealed class CreateInstanceHandler(
    HubDbContext dbContext,
    ICurrentUserService currentUserService,
    IProvisioningQueue provisioningQueue,
    InstanceCreationService instanceCreationService,
    ISystemConfigService systemConfigService,
    IOptions<StripeOptions> stripeOptions)
    : IRequestHandler<CreateInstanceCommand, Result<CreateInstanceResponse>>,
      IValidatable<CreateInstanceCommand>
{
    public Error? Validate(CreateInstanceCommand request)
    {
        var subdomainError = ValidationHelpers.ValidateSubdomain(request.Subdomain);
        if (subdomainError != null)
            return subdomainError;

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return Error.Validation("VALIDATION_FAILED", "DisplayName is required");

        if (request.DisplayName.Length > 255)
            return Error.Validation("VALIDATION_FAILED", "DisplayName must not exceed 255 characters");

        if (!Enum.IsDefined(request.Tier))
            return Error.Validation("VALIDATION_FAILED", "Invalid tier");

        if (request.Tier != InstanceTier.Free && !stripeOptions.Value.IsConfigured)
            return Error.Validation("PAID_TIER_UNAVAILABLE", "Payment processing is not configured. Only the free tier is available.");

        if (request.MediaEnabled && !stripeOptions.Value.IsConfigured)
            return Error.Validation("MEDIA_UNAVAILABLE", "Payment processing is not configured. Voice & video requires a paid add-on.");

        return null;
    }

    public async Task<Result<CreateInstanceResponse>> Handle(CreateInstanceCommand request, CancellationToken cancellationToken)
    {
        var userIdResult = currentUserService.GetCurrentUserId();
        if (userIdResult.IsFailure) return userIdResult.Error!;
        var userId = userIdResult.Value;

        var requestsPaidFeatures = request.Tier != InstanceTier.Free || request.MediaEnabled;
        if (requestsPaidFeatures)
        {
            var systemConfig = await systemConfigService.GetAsync(cancellationToken);
            if (systemConfig.PaidServersDisabled)
                return Error.Validation("PAID_SERVERS_DISABLED", "Creation of new paid servers is currently disabled.");
        }

        // Generate admin password if not provided
        var adminPassword = !string.IsNullOrWhiteSpace(request.AdminPassword)
            ? request.AdminPassword
            : Guid.NewGuid().ToString("N")[..16];

        var result = await instanceCreationService.CreateAsync(
            userId,
            request.Subdomain,
            request.DisplayName,
            adminPassword,
            request.Tier,
            request.MediaEnabled,
            skipCaptcha: false,
            request.CaptchaId,
            request.CaptchaAnswer,
            cancellationToken);

        if (result.IsFailure) return result.Error!;
        var instance = result.Value;

        await dbContext.SaveChangesAsync(cancellationToken);

        // Enqueue for background provisioning
        await provisioningQueue.EnqueueAsync(instance.Id, cancellationToken);

        return new CreateInstanceResponse(
            instance.Id.ToString(),
            instance.Domain,
            instance.DisplayName,
            InstanceStatus.Pending.ToString(),
            adminPassword
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/hub/instances", async (
            CreateInstanceCommand command,
            CreateInstanceHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(command, ct, success =>
                Results.Created($"/api/v1/hub/instances/{success.InstanceId}", success));
        })
        .RequireAuthorization(Policies.User)
        .Produces<CreateInstanceResponse>(201)
        .WithName("CreateInstance")
        .WithTags("Instances");
    }
}
