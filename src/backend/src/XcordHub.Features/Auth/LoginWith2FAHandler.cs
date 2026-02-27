using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BCrypt.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Auth;

public sealed record LoginWith2FARequest(
    string Email,
    string Password,
    string Code
);

public sealed record LoginWith2FAResponse(string UserId, string Username, string DisplayName, string Email, string AccessToken, string RefreshToken);

public sealed record LoginWith2FAApiResponse(string UserId, string Username, string DisplayName, string Email, string AccessToken);

public sealed class LoginWith2FAHandler(
    HubDbContext dbContext,
    IEncryptionService encryptionService,
    IJwtService jwtService,
    SnowflakeId snowflakeGenerator,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<LoginWith2FARequest, Result<LoginWith2FAResponse>>, IValidatable<LoginWith2FARequest>
{
    private const int MaxCumulativeTwoFactorFailures = 10;
    private static readonly TimeSpan TwoFactorLockoutDuration = TimeSpan.FromMinutes(30);

    public Error? Validate(LoginWith2FARequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return Error.Validation("VALIDATION_FAILED", "Email is required");

        if (!ValidationHelpers.IsValidEmail(request.Email))
            return Error.Validation("VALIDATION_FAILED", "Invalid email format");

        if (string.IsNullOrWhiteSpace(request.Password))
            return Error.Validation("VALIDATION_FAILED", "Password is required");

        if (string.IsNullOrWhiteSpace(request.Code))
            return Error.Validation("VALIDATION_FAILED", "Verification code is required");

        if (request.Code.Length != 6)
            return Error.Validation("VALIDATION_FAILED", "Verification code must be 6 digits");

        if (!Regex.IsMatch(request.Code, @"^\d{6}$"))
            return Error.Validation("VALIDATION_FAILED", "Verification code must be numeric");

        return null;
    }

    private LoginAttempt CreateLoginAttempt(string email, string? failureReason = null, long? userId = null)
        => LoginAttemptRecorder.Create(snowflakeGenerator, httpContextAccessor, email, failureReason, userId);

    public async Task<Result<LoginWith2FAResponse>> Handle(LoginWith2FARequest request, CancellationToken cancellationToken)
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

        // 2FA must be enabled for this endpoint
        if (!user.TwoFactorEnabled || string.IsNullOrEmpty(user.TwoFactorSecret))
        {
            dbContext.LoginAttempts.Add(CreateLoginAttempt(request.Email, "2FA_NOT_ENABLED", user.Id));
            await dbContext.SaveChangesAsync(cancellationToken);
            return Error.Validation("2FA_NOT_ENABLED", "Two-factor authentication is not enabled on this account");
        }

        // Check cumulative 2FA failure lockout
        if (user.TwoFactorLockedAt != null)
        {
            var lockExpiry = user.TwoFactorLockedAt.Value.Add(TwoFactorLockoutDuration);
            if (DateTimeOffset.UtcNow < lockExpiry)
            {
                dbContext.LoginAttempts.Add(CreateLoginAttempt(request.Email, "TWO_FACTOR_LOCKED", user.Id));
                await dbContext.SaveChangesAsync(cancellationToken);
                return Error.Forbidden("TWO_FACTOR_LOCKED",
                    "Account is temporarily locked due to too many failed 2FA attempts. Please try again later.");
            }

            // Lockout has expired -- reset counters
            user.TwoFactorFailureCount = 0;
            user.TwoFactorLockedAt = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // Validate TOTP code
        if (!ValidateTotpCode(user.TwoFactorSecret, request.Code))
        {
            // Track cumulative 2FA failures
            user.TwoFactorFailureCount++;
            if (user.TwoFactorFailureCount >= MaxCumulativeTwoFactorFailures)
            {
                user.TwoFactorLockedAt = DateTimeOffset.UtcNow;
                dbContext.LoginAttempts.Add(CreateLoginAttempt(request.Email, "TWO_FACTOR_LOCKED", user.Id));
                await dbContext.SaveChangesAsync(cancellationToken);
                return Error.Forbidden("TWO_FACTOR_LOCKED",
                    "Account is temporarily locked due to too many failed 2FA attempts. Please try again later.");
            }

            dbContext.LoginAttempts.Add(CreateLoginAttempt(request.Email, "INVALID_2FA_CODE", user.Id));
            await dbContext.SaveChangesAsync(cancellationToken);
            return Error.Validation("INVALID_CODE", "Invalid verification code");
        }

        // Successful 2FA verification -- reset failure counters
        user.TwoFactorFailureCount = 0;
        user.TwoFactorLockedAt = null;

        // Update last login timestamp
        user.LastLoginAt = DateTimeOffset.UtcNow;

        // Create refresh token (30 days)
        var refreshTokenValue = TokenHelper.GenerateToken();
        var refreshTokenHash = TokenHelper.HashToken(refreshTokenValue);
        var now = DateTimeOffset.UtcNow;

        var refreshToken = new RefreshToken
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

        return new LoginWith2FAResponse(user.Id.ToString(), user.Username, user.DisplayName, email, accessToken, refreshTokenValue);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/auth/2fa/login", async (
            LoginWith2FARequest request,
            LoginWith2FAHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var result = await handler.ExecuteAsync(request, ct, success =>
            {
                AuthCookieHelper.SetRefreshTokenCookie(httpContext, success.RefreshToken);

                return Results.Ok(new LoginWith2FAApiResponse(
                    success.UserId,
                    success.Username,
                    success.DisplayName,
                    success.Email,
                    success.AccessToken));
            });

            return result;
        })
        .AllowAnonymous()
        .Produces<LoginWith2FAApiResponse>(200)
        .WithName("LoginWith2FA")
        .WithTags("Auth");
    }

    private static bool ValidateTotpCode(string base32Secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6)
        {
            return false;
        }

        var secretBytes = Base32Decode(base32Secret);
        var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timeStep = unixTime / 30;

        // Check current time window and +/-1 window for clock skew
        for (long offset = -1; offset <= 1; offset++)
        {
            var counter = timeStep + offset;
            var expectedCode = GenerateTotpCode(secretBytes, counter);
            if (expectedCode == code)
            {
                return true;
            }
        }

        return false;
    }

    private static string GenerateTotpCode(byte[] secret, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        using var hmac = new HMACSHA256(secret);
        var hash = hmac.ComputeHash(counterBytes);

        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);

        var otp = binary % 1000000;
        return otp.ToString("D6");
    }

    private static byte[] Base32Decode(string base32)
    {
        const string base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        base32 = base32.ToUpperInvariant().TrimEnd('=');

        var numBytes = base32.Length * 5 / 8;
        var result = new byte[numBytes];

        var bitBuffer = 0;
        var bitsInBuffer = 0;
        var resultIndex = 0;

        foreach (var c in base32)
        {
            var value = base32Chars.IndexOf(c);
            if (value < 0)
            {
                continue;
            }

            bitBuffer = (bitBuffer << 5) | value;
            bitsInBuffer += 5;

            if (bitsInBuffer >= 8)
            {
                result[resultIndex++] = (byte)(bitBuffer >> (bitsInBuffer - 8));
                bitsInBuffer -= 8;
            }
        }

        return result;
    }
}
