using BCrypt.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using XcordHub.Entities;
using XcordHub.Infrastructure.Options;
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
    SnowflakeIdGenerator snowflakeGenerator,
    IHttpContextAccessor httpContextAccessor,
    IConnectionMultiplexer redis,
    IOptions<RedisOptions> redisOptions,
    IOptions<AuthOptions> authOptions)
    : IRequestHandler<LoginRequest, Result<LoginResponse>>, IValidatable<LoginRequest>
{
    private readonly int _maxFailedAttempts = authOptions.Value.MaxLoginAttemptsPerWindow;
    private readonly TimeSpan _lockoutDuration = TimeSpan.FromMinutes(authOptions.Value.LoginAttemptWindowMinutes);
    private readonly int _refreshTokenDays = authOptions.Value.JwtRefreshTokenDays;

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
        var emailHashHex = Convert.ToHexString(emailHash);
        var rateLimitKey = $"{redisOptions.Value.ChannelPrefix}:login-attempts:{emailHashHex}";

        // Check per-account brute-force counter before verifying the password
        var db = redis.GetDatabase();
        var currentCount = (long?)await db.StringGetAsync(rateLimitKey).ConfigureAwait(false);
        if (currentCount >= _maxFailedAttempts)
        {
            var ttl = await db.KeyTimeToLiveAsync(rateLimitKey).ConfigureAwait(false);
            var retryAfterSeconds = ttl.HasValue ? (int)Math.Ceiling(ttl.Value.TotalSeconds) : (int)_lockoutDuration.TotalSeconds;
            // Failure-branch SaveChanges (audit-only insert) stays outside the happy-path
            // transaction; it is a single atomic write and is acceptable to leave standalone.
            dbContext.LoginAttempts.Add(CreateLoginAttempt(request.Email, "LOGIN_RATE_LIMITED"));
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Error.RateLimited("LOGIN_RATE_LIMITED", retryAfterSeconds.ToString());
        }

        var user = await dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.EmailHash == emailHash, cancellationToken);

        if (user == null)
        {
            await IncrementAttemptCounterAsync(db, rateLimitKey, _lockoutDuration).ConfigureAwait(false);
            dbContext.LoginAttempts.Add(CreateLoginAttempt(request.Email, "INVALID_CREDENTIALS"));
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Error.Validation("INVALID_CREDENTIALS", "Invalid email or password");
        }

        // Verify password - offloaded to thread pool to avoid starvation
        if (!await Task.Run(() => BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash)))
        {
            await IncrementAttemptCounterAsync(db, rateLimitKey, _lockoutDuration).ConfigureAwait(false);
            dbContext.LoginAttempts.Add(CreateLoginAttempt(request.Email, "INVALID_CREDENTIALS", user.Id));
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Error.Validation("INVALID_CREDENTIALS", "Invalid email or password");
        }

        // Successful login - clear the brute-force counter
        await db.KeyDeleteAsync(rateLimitKey).ConfigureAwait(false);

        // Check if account is disabled
        if (user.IsDisabled)
        {
            dbContext.LoginAttempts.Add(CreateLoginAttempt(request.Email, "ACCOUNT_DISABLED", user.Id));
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Error.Forbidden("ACCOUNT_DISABLED", "Account is disabled");
        }

        // If 2FA is enabled, reject with a code the client uses to prompt for TOTP
        if (user.TwoFactorEnabled)
        {
            dbContext.LoginAttempts.Add(CreateLoginAttempt(request.Email, "2FA_REQUIRED", user.Id));
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Error.Forbidden("2FA_REQUIRED", "Two-factor authentication is required");
        }

        // Happy-path mutations (LastLoginAt update + RefreshToken insert + success LoginAttempt
        // insert) all run in a single transaction so a partial failure cannot leave the user
        // with, say, a recorded login attempt but no usable refresh token.
        var refreshTokenValue = TokenHelper.GenerateToken();
        var refreshTokenHash = TokenHelper.HashToken(refreshTokenValue);
        var now = DateTimeOffset.UtcNow;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Update last login timestamp
        user.LastLoginAt = now;

        // Create refresh token (30 days)
        var refreshToken = new Entities.RefreshToken
        {
            Id = snowflakeGenerator.NextId(),
            TokenHash = refreshTokenHash,
            HubUserId = user.Id,
            ExpiresAt = now.AddDays(_refreshTokenDays),
            CreatedAt = now
        };

        dbContext.RefreshTokens.Add(refreshToken);

        // Record successful login attempt
        dbContext.LoginAttempts.Add(CreateLoginAttempt(request.Email, null, user.Id));

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        // Generate JWT access token only after the transaction commits successfully; we never
        // hand a token to a client that does not have a persisted refresh-token row.
        var accessToken = jwtService.GenerateAccessToken(user.Id, user.IsAdmin);

        var email = encryptionService.Decrypt(user.Email);

        return new LoginResponse(user.Id.ToString(), user.Username, user.DisplayName, email, accessToken, refreshTokenValue);
    }

    private static async Task IncrementAttemptCounterAsync(IDatabase db, string key, TimeSpan lockoutDuration)
    {
        var count = await db.StringIncrementAsync(key).ConfigureAwait(false);
        if (count == 1)
        {
            // First failure - set the TTL so the lockout window starts now
            await db.KeyExpireAsync(key, lockoutDuration).ConfigureAwait(false);
        }
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/auth/login", async (
            LoginRequest request,
            LoginHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            // Run validation first
            var validationError = handler.Validate(request);
            if (validationError is not null)
                return Results.Problem(statusCode: validationError.StatusCode, title: validationError.Code, detail: validationError.Message);

            var result = await handler.Handle(request, ct).ConfigureAwait(false);

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
                error =>
                {
                    if (error.StatusCode == 429 && error.Code == "LOGIN_RATE_LIMITED"
                        && int.TryParse(error.Message, out var retryAfter))
                    {
                        httpContext.Response.Headers["Retry-After"] = retryAfter.ToString();
                        return Results.Problem(
                            statusCode: 429,
                            title: error.Code,
                            detail: $"Too many failed login attempts. Please wait {retryAfter} second(s) before trying again.");
                    }

                    return Results.Problem(
                        statusCode: error.StatusCode,
                        title: error.Code,
                        detail: error.Message);
                });
        })
        .AllowAnonymous()
        .Produces<LoginApiResponse>(200)
        .WithName("Login")
        .WithTags("Auth");
    }

}
