using BCrypt.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Auth;

public sealed record SetupRequest(
    string Username,
    string Email,
    string Password
);

public sealed class SetupHandler(
    HubDbContext dbContext,
    IEncryptionService encryptionService,
    IJwtService jwtService,
    SnowflakeIdGenerator snowflakeGenerator,
    IHttpContextAccessor httpContextAccessor,
    IOptions<AuthOptions> authOptions)
    : IEndpoint
{
    private readonly AuthOptions _authOptions = authOptions.Value;

    public async Task<Result<LoginResponse>> Handle(SetupRequest request, CancellationToken cancellationToken)
    {
        // Use serializable transaction to prevent TOCTOU race on first-boot setup
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);

        try
        {
            // Guard: if any user already exists, setup has already been completed
            var hasUsers = await dbContext.HubUsers.AnyAsync(cancellationToken);
            if (hasUsers)
            {
                return Error.BadRequest("SETUP_ALREADY_COMPLETED", "Setup already completed");
            }

            // Hash password - offloaded to thread pool to avoid starvation
            var passwordHash = await Task.Run(() => BCrypt.Net.BCrypt.HashPassword(request.Password, _authOptions.BcryptWorkFactor));

            // Encrypt email and compute HMAC for lookup
            var encryptedEmail = encryptionService.Encrypt(request.Email.ToLowerInvariant());
            var emailHash = encryptionService.ComputeHmac(request.Email.ToLowerInvariant());

            var userId = snowflakeGenerator.NextId();
            var now = DateTimeOffset.UtcNow;

            var adminUser = new HubUser
            {
                Id = userId,
                Username = request.Username,
                DisplayName = request.Username,
                Email = encryptedEmail,
                EmailHash = emailHash,
                PasswordHash = passwordHash,
                IsAdmin = true,
                IsDisabled = false,
                CreatedAt = now,
                LastLoginAt = now
            };

            dbContext.HubUsers.Add(adminUser);

            // Create refresh token (30 days)
            var refreshTokenValue = TokenHelper.GenerateToken();
            var refreshTokenHash = TokenHelper.HashToken(refreshTokenValue);

            var refreshToken = new RefreshToken
            {
                Id = snowflakeGenerator.NextId(),
                TokenHash = refreshTokenHash,
                HubUserId = userId,
                ExpiresAt = now.AddDays(30),
                CreatedAt = now
            };

            dbContext.RefreshTokens.Add(refreshToken);

            // Record login attempt for the initial setup
            dbContext.LoginAttempts.Add(
                LoginAttemptRecorder.Create(snowflakeGenerator, httpContextAccessor, request.Email, null, userId));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            // Generate JWT access token
            var accessToken = jwtService.GenerateAccessToken(userId, isAdmin: true);

            return new LoginResponse(userId.ToString(), adminUser.Username, adminUser.DisplayName, request.Email, accessToken, refreshTokenValue);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/setup", async (
            SetupRequest request,
            SetupHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username))
                return Results.Problem(statusCode: 400, title: "VALIDATION_FAILED", detail: "Username is required");

            if (string.IsNullOrWhiteSpace(request.Email))
                return Results.Problem(statusCode: 400, title: "VALIDATION_FAILED", detail: "Email is required");

            if (!ValidationHelpers.IsValidEmail(request.Email))
                return Results.Problem(statusCode: 400, title: "VALIDATION_FAILED", detail: "Invalid email format");

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
                return Results.Problem(statusCode: 400, title: "VALIDATION_FAILED", detail: "Password must be at least 8 characters");

            var result = await handler.Handle(request, ct);

            return result.Match(
                success =>
                {
                    AuthCookieHelper.SetRefreshTokenCookie(httpContext, success.RefreshToken);

                    return Results.Ok(new LoginApiResponse(
                        success.UserId,
                        success.Username,
                        success.DisplayName,
                        success.Email,
                        success.AccessToken));
                },
                error => Results.Problem(
                    statusCode: error.StatusCode,
                    title: error.Code,
                    detail: error.Message));
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth-register")
        .Produces<LoginApiResponse>(200)
        .WithName("Setup")
        .WithTags("Setup");
    }
}
