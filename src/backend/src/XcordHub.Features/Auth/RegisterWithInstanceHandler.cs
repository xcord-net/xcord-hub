using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordHub.Entities;
using Microsoft.Extensions.Options;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Auth;

public sealed record RegisterWithInstanceCommand(
    // Account fields
    string Username,
    string DisplayName,
    string Email,
    string Password,
    // Instance fields
    string Subdomain,
    string InstanceDisplayName,
    InstanceTier Tier = InstanceTier.Free,
    bool MediaEnabled = false,
    // Captcha
    string? CaptchaId = null,
    string? CaptchaAnswer = null
);

public sealed record RegisterWithInstanceResponse(
    string UserId,
    string Username,
    string AccessToken,
    string InstanceId,
    string Domain,
    string InstanceDisplayName,
    string Status
);

public sealed class RegisterWithInstanceHandler(
    HubDbContext dbContext,
    UserRegistrationService userRegistrationService,
    InstanceCreationService instanceCreationService,
    IProvisioningQueue provisioningQueue,
    SnowflakeIdGenerator snowflakeGenerator,
    IHttpContextAccessor httpContextAccessor,
    IOptions<StripeOptions> stripeOptions)
    : IRequestHandler<RegisterWithInstanceCommand, Result<RegisterWithInstanceInternalResponse>>,
      IValidatable<RegisterWithInstanceCommand>
{
    public Error? Validate(RegisterWithInstanceCommand request)
    {
        // Account validation (same rules as RegisterHandler)
        if (string.IsNullOrWhiteSpace(request.Username))
            return Error.Validation("VALIDATION_FAILED", "Username is required");

        if (request.Username.Length > 32)
            return Error.Validation("VALIDATION_FAILED", "Username must not exceed 32 characters");

        if (!Regex.IsMatch(request.Username, "^[a-zA-Z0-9_-]+$"))
            return Error.Validation("VALIDATION_FAILED", "Username can only contain letters, numbers, underscores, and hyphens");

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return Error.Validation("VALIDATION_FAILED", "Display name is required");

        if (request.DisplayName.Length > 32)
            return Error.Validation("VALIDATION_FAILED", "Display name must not exceed 32 characters");

        if (string.IsNullOrWhiteSpace(request.Email))
            return Error.Validation("VALIDATION_FAILED", "Email is required");

        if (!ValidationHelpers.IsValidEmail(request.Email))
            return Error.Validation("VALIDATION_FAILED", "Invalid email format");

        if (request.Email.Length > 255)
            return Error.Validation("VALIDATION_FAILED", "Email must not exceed 255 characters");

        if (string.IsNullOrWhiteSpace(request.Password))
            return Error.Validation("VALIDATION_FAILED", "Password is required");

        if (request.Password.Length < 8 || request.Password.Length > 128)
            return Error.Validation("VALIDATION_FAILED", "Password must be between 8 and 128 characters");

        // Instance validation (same rules as CreateInstanceHandler)
        var subdomainError = ValidationHelpers.ValidateSubdomain(request.Subdomain);
        if (subdomainError != null)
            return subdomainError;

        if (string.IsNullOrWhiteSpace(request.InstanceDisplayName))
            return Error.Validation("VALIDATION_FAILED", "Instance display name is required");

        if (request.InstanceDisplayName.Length > 255)
            return Error.Validation("VALIDATION_FAILED", "Instance display name must not exceed 255 characters");

        if (!Enum.IsDefined(request.Tier))
            return Error.Validation("VALIDATION_FAILED", "Invalid tier");

        if (request.Tier != InstanceTier.Free && !stripeOptions.Value.IsConfigured)
            return Error.Validation("PAID_TIER_UNAVAILABLE", "Payment processing is not configured. Only the free tier is available.");

        if (request.MediaEnabled && !stripeOptions.Value.IsConfigured)
            return Error.Validation("MEDIA_UNAVAILABLE", "Payment processing is not configured. Voice & video requires a paid add-on.");

        return null;
    }

    public async Task<Result<RegisterWithInstanceInternalResponse>> Handle(
        RegisterWithInstanceCommand request, CancellationToken cancellationToken)
    {
        // 1. Register user (captcha validated here)
        var regResult = await userRegistrationService.RegisterAsync(
            request.Username,
            request.DisplayName,
            request.Email,
            request.Password,
            request.CaptchaId,
            request.CaptchaAnswer,
            cancellationToken);

        if (regResult.IsFailure)
            return regResult.Error;

        var reg = regResult.Value;

        // 2. Create instance (skip captcha - already validated above, reuse account password as admin password)
        var instanceResult = await instanceCreationService.CreateAsync(
            reg.User.Id,
            request.Subdomain,
            request.InstanceDisplayName,
            request.Password,
            request.Tier,
            request.MediaEnabled,
            skipCaptcha: true,
            captchaId: null,
            captchaAnswer: null,
            cancellationToken);

        if (instanceResult.IsFailure)
            return instanceResult.Error;

        var instance = instanceResult.Value;

        // 3. Record login attempt
        dbContext.LoginAttempts.Add(
            LoginAttemptRecorder.Create(snowflakeGenerator, httpContextAccessor, request.Email, null, reg.User.Id));

        // 4. Single atomic save
        await dbContext.SaveChangesAsync(cancellationToken);

        // 5. Enqueue provisioning (after save - EnqueueAsync calls its own SaveChangesAsync)
        await provisioningQueue.EnqueueAsync(instance.Id, cancellationToken);

        return new RegisterWithInstanceInternalResponse(
            reg.User.Id.ToString(),
            reg.User.Username,
            reg.AccessToken,
            reg.RefreshToken,
            instance.Id.ToString(),
            instance.Domain,
            instance.DisplayName,
            InstanceStatus.Pending.ToString());
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/hub/register-with-instance", async (
            RegisterWithInstanceCommand command,
            RegisterWithInstanceHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(command, ct, success =>
            {
                AuthCookieHelper.SetRefreshTokenCookie(httpContext, success.RefreshToken);

                return Results.Created($"/api/v1/hub/instances/{success.InstanceId}",
                    new RegisterWithInstanceResponse(
                        success.UserId,
                        success.Username,
                        success.AccessToken,
                        success.InstanceId,
                        success.Domain,
                        success.InstanceDisplayName,
                        success.Status));
            });
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth-register")
        .Produces<RegisterWithInstanceResponse>(201)
        .WithName("RegisterWithInstance")
        .WithTags("Auth");
    }
}

// Internal response that includes RefreshToken for cookie setting (not exposed in API response)
public sealed record RegisterWithInstanceInternalResponse(
    string UserId,
    string Username,
    string AccessToken,
    string RefreshToken,
    string InstanceId,
    string Domain,
    string InstanceDisplayName,
    string Status
);
