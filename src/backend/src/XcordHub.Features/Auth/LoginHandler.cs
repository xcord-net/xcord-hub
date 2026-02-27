using BCrypt.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Auth;

public sealed record LoginRequest(
    string Email,
    string Password
);

public sealed record LoginResponse(string UserId, string Username, string DisplayName, string Email, string AccessToken, string RefreshToken);

public sealed record LoginApiResponse(string UserId, string Username, string DisplayName, string Email, string AccessToken);

public sealed class LoginHandler(
    HubDbContext dbContext,
    IEncryptionService encryptionService,
    IJwtService jwtService,
    SnowflakeId snowflakeGenerator,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<LoginRequest, Result<LoginResponse>>, IValidatable<LoginRequest>
{
    public Error? Validate(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return Error.Validation("VALIDATION_FAILED", "Email is required");

        if (!ValidationHelpers.IsValidEmail(request.Email))
            return Error.Validation("VALIDATION_FAILED", "Invalid email format");

        if (string.IsNullOrWhiteSpace(request.Password))
            return Error.Validation("VALIDATION_FAILED", "Password is required");

        return null;
    }

    private LoginAttempt CreateLoginAttempt(string email, string? failureReason = null, long? userId = null)
        => LoginAttemptRecorder.Create(snowflakeGenerator, httpContextAccessor, email, failureReason, userId);

    public async Task<Result<LoginResponse>> Handle(LoginRequest request, CancellationToken cancellationToken)
    {
        // Find user by EmailHash
        var emailHash = encryptionService.ComputeHmac(request.Email.ToLowerInvariant());
        var user = await dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.EmailHash == emailHash, cancellationToken);

        if (user == null)
        {
            dbContext.LoginAttempts.Add(CreateLoginAttempt(request.Email, "INVALID_CREDENTIALS"));
            await dbContext.SaveChangesAsync(cancellationToken);
            return Error.Validation("INVALID_CREDENTIALS", "Invalid email or password");
        }

        // Verify password
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            dbContext.LoginAttempts.Add(CreateLoginAttempt(request.Email, "INVALID_CREDENTIALS", user.Id));
            await dbContext.SaveChangesAsync(cancellationToken);
            return Error.Validation("INVALID_CREDENTIALS", "Invalid email or password");
        }

        // Check if account is disabled
        if (user.IsDisabled)
        {
            dbContext.LoginAttempts.Add(CreateLoginAttempt(request.Email, "ACCOUNT_DISABLED", user.Id));
            await dbContext.SaveChangesAsync(cancellationToken);
            return Error.Forbidden("ACCOUNT_DISABLED", "Account is disabled");
        }

        // If 2FA is enabled, reject with a code the client uses to prompt for TOTP
        if (user.TwoFactorEnabled)
        {
            dbContext.LoginAttempts.Add(CreateLoginAttempt(request.Email, "2FA_REQUIRED", user.Id));
            await dbContext.SaveChangesAsync(cancellationToken);
            return Error.Forbidden("2FA_REQUIRED", "Two-factor authentication is required");
        }

        // Update last login timestamp
        user.LastLoginAt = DateTimeOffset.UtcNow;

        // Create refresh token (30 days)
        var refreshTokenValue = TokenHelper.GenerateToken();
        var refreshTokenHash = TokenHelper.HashToken(refreshTokenValue);
        var now = DateTimeOffset.UtcNow;

        var refreshToken = new Entities.RefreshToken
        {
            Id = snowflakeGenerator.NextId(),
            TokenHash = refreshTokenHash,
            HubUserId = user.Id,
            ExpiresAt = now.AddDays(30),
            CreatedAt = now
        };

        dbContext.RefreshTokens.Add(refreshToken);

        // Record successful login attempt
        dbContext.LoginAttempts.Add(CreateLoginAttempt(request.Email, null, user.Id));

        await dbContext.SaveChangesAsync(cancellationToken);

        // Generate JWT access token
        var accessToken = jwtService.GenerateAccessToken(user.Id, user.IsAdmin);

        var email = encryptionService.Decrypt(user.Email);

        return new LoginResponse(user.Id.ToString(), user.Username, user.DisplayName, email, accessToken, refreshTokenValue);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/auth/login", async (
            LoginRequest request,
            LoginHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var result = await handler.ExecuteAsync(request, ct, success =>
            {
                AuthCookieHelper.SetRefreshTokenCookie(httpContext, success.RefreshToken);

                return Results.Ok(new LoginApiResponse(
                    success.UserId,
                    success.Username,
                    success.DisplayName,
                    success.Email,
                    success.AccessToken));
            });

            return result;
        })
        .AllowAnonymous()
        .Produces<LoginApiResponse>(200)
        .WithName("Login")
        .WithTags("Auth");
    }

}
