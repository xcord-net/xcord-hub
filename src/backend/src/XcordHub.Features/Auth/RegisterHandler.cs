using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BCrypt.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Auth;

public sealed record RegisterRequest(
    string Username,
    string DisplayName,
    string Email,
    string Password
);

public sealed record RegisterResponse(long UserId, string Username, string DisplayName, string Email, string AccessToken, string RefreshToken);

public sealed class RegisterHandler(
    HubDbContext dbContext,
    IEncryptionService encryptionService,
    IJwtService jwtService,
    SnowflakeId snowflakeGenerator)
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

        if (!Regex.IsMatch(request.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
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
        // Check if username already exists
        var usernameExists = await dbContext.HubUsers
            .AnyAsync(u => u.Username == request.Username, cancellationToken);

        if (usernameExists)
        {
            return Error.Conflict("USERNAME_TAKEN", "Username is already taken");
        }

        // Check if email already exists (by EmailHash)
        var emailHash = encryptionService.ComputeHmac(request.Email.ToLowerInvariant());
        var emailExists = await dbContext.HubUsers
            .AnyAsync(u => u.EmailHash == emailHash, cancellationToken);

        if (emailExists)
        {
            return Error.Conflict("EMAIL_TAKEN", "Email is already registered");
        }

        // Hash password (BCrypt, work factor 12)
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12);

        // Encrypt email
        var encryptedEmail = encryptionService.Encrypt(request.Email.ToLowerInvariant());

        // Create user
        var userId = snowflakeGenerator.NextId();
        var now = DateTimeOffset.UtcNow;

        var user = new Entities.HubUser
        {
            Id = userId,
            Username = request.Username,
            DisplayName = request.DisplayName,
            Email = encryptedEmail,
            EmailHash = emailHash,
            PasswordHash = passwordHash,
            IsAdmin = false,
            IsDisabled = false,
            CreatedAt = now,
            LastLoginAt = now
        };

        dbContext.HubUsers.Add(user);

        // Create refresh token (30 days)
        var refreshTokenValue = GenerateRefreshToken();
        var refreshTokenHash = HashToken(refreshTokenValue);
        var refreshToken = new Entities.RefreshToken
        {
            Id = snowflakeGenerator.NextId(),
            TokenHash = refreshTokenHash,
            HubUserId = userId,
            ExpiresAt = now.AddDays(30),
            CreatedAt = now
        };

        dbContext.RefreshTokens.Add(refreshToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        // Generate JWT access token
        var accessToken = jwtService.GenerateAccessToken(userId, user.IsAdmin);

        return new RegisterResponse(userId, user.Username, user.DisplayName, request.Email, accessToken, refreshTokenValue);
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
                // Set httpOnly cookie with refresh token
                httpContext.Response.Cookies.Append("refresh_token", success.RefreshToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Expires = DateTimeOffset.UtcNow.AddDays(30)
                });

                return Results.Ok(new
                {
                    userId = success.UserId,
                    username = success.Username,
                    displayName = success.DisplayName,
                    email = success.Email,
                    accessToken = success.AccessToken
                });
            });

            return result;
        })
        .AllowAnonymous()
        .WithName("Register")
        .WithTags("Auth");
    }

    private static string GenerateRefreshToken()
    {
        var randomBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hashBytes);
    }
}
