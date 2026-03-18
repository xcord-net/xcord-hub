using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Auth;

public sealed record RegisterRequest(
    string Username,
    string DisplayName,
    string Email,
    string Password,
    string? CaptchaId = null,
    string? CaptchaAnswer = null
);

public sealed record RegisterResponse(string UserId, string Username, string DisplayName, string Email, string AccessToken, string RefreshToken);

public sealed record RegisterApiResponse(string UserId, string Username, string DisplayName, string Email, string AccessToken);

public sealed class RegisterHandler(
    HubDbContext dbContext,
    UserRegistrationService userRegistrationService,
    SnowflakeIdGenerator snowflakeGenerator,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<RegisterRequest, Result<RegisterResponse>>, IValidatable<RegisterRequest>
{
    public Error? Validate(RegisterRequest request)
    {
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

        return null;
    }

    public async Task<Result<RegisterResponse>> Handle(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await userRegistrationService.RegisterAsync(
            request.Username,
            request.DisplayName,
            request.Email,
            request.Password,
            request.CaptchaId,
            request.CaptchaAnswer,
            cancellationToken);

        if (result.IsFailure)
            return result.Error;

        var reg = result.Value;

        // Record login attempt (registration counts as a successful login)
        dbContext.LoginAttempts.Add(
            LoginAttemptRecorder.Create(snowflakeGenerator, httpContextAccessor, request.Email, null, reg.User.Id));

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterResponse(reg.User.Id.ToString(), reg.User.Username, reg.User.DisplayName, request.Email, reg.AccessToken, reg.RefreshToken);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/auth/register", async (
            RegisterRequest request,
            RegisterHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var result = await handler.ExecuteAsync(request, ct, success =>
            {
                AuthCookieHelper.SetRefreshTokenCookie(httpContext, success.RefreshToken);

                return Results.Ok(new RegisterApiResponse(
                    success.UserId,
                    success.Username,
                    success.DisplayName,
                    success.Email,
                    success.AccessToken));
            });

            return result;
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth-register")
        .Produces<RegisterApiResponse>(200)
        .WithName("Register")
        .WithTags("Auth");
    }

}
