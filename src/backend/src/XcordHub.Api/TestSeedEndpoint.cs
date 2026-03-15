using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using XcordHub.Entities;
using XcordHub.Features.Auth;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Api;

public static class TestSeedEndpoint
{
    public sealed record SeedUserRequest(
        string Username,
        string DisplayName,
        string Email,
        string Password
    );

    public sealed record SeedUserResponse(
        string UserId,
        string Username,
        string AccessToken,
        string RefreshToken
    );

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/v1/test/seed-user", async (
            SeedUserRequest request,
            HttpContext httpContext,
            HubDbContext dbContext,
            IEncryptionService encryptionService,
            IJwtService jwtService,
            SnowflakeIdGenerator snowflakeGenerator,
            IOptions<AuthOptions> authOptions,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            // Verify X-Test-Key header
            var expectedKey = configuration["TestSeed:Key"];
            var providedKey = httpContext.Request.Headers["X-Test-Key"].FirstOrDefault();

            if (string.IsNullOrEmpty(expectedKey) || providedKey != expectedKey)
                return Results.Problem(statusCode: 403, title: "FORBIDDEN", detail: "Invalid or missing X-Test-Key header");

            var now = DateTimeOffset.UtcNow;
            var workFactor = authOptions.Value.BcryptWorkFactor;

            // Check if username already exists - if so, log in idempotently
            var existingUser = await dbContext.HubUsers
                .FirstOrDefaultAsync(u => u.Username == request.Username, ct);

            if (existingUser != null)
            {
                // Verify password matches before re-issuing tokens
                var passwordMatches = await Task.Run(() => BCrypt.Net.BCrypt.Verify(request.Password, existingUser.PasswordHash));
                if (!passwordMatches)
                    return Results.Problem(statusCode: 409, title: "CONFLICT", detail: "Username exists but password does not match");

                // Issue new refresh token for the existing user
                var existingRefreshValue = TokenHelper.GenerateToken();
                var existingRefreshHash = TokenHelper.HashToken(existingRefreshValue);
                var existingRefreshToken = new RefreshToken
                {
                    Id = snowflakeGenerator.NextId(),
                    TokenHash = existingRefreshHash,
                    HubUserId = existingUser.Id,
                    ExpiresAt = now.AddDays(30),
                    CreatedAt = now
                };

                dbContext.RefreshTokens.Add(existingRefreshToken);
                await dbContext.SaveChangesAsync(ct);

                var existingAccessToken = jwtService.GenerateAccessToken(existingUser.Id, existingUser.IsAdmin);
                AuthCookieHelper.SetRefreshTokenCookie(httpContext, existingRefreshValue);

                return Results.Ok(new SeedUserResponse(
                    existingUser.Id.ToString(),
                    existingUser.Username,
                    existingAccessToken,
                    existingRefreshValue));
            }

            // Encrypt email and compute hash
            var encryptedEmail = encryptionService.Encrypt(request.Email.ToLowerInvariant());
            var emailHash = encryptionService.ComputeHmac(request.Email.ToLowerInvariant());

            // Hash password - offloaded to thread pool to avoid starvation
            var passwordHash = await Task.Run(() => BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor));

            // Create user
            var userId = snowflakeGenerator.NextId();

            var user = new HubUser
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
            await dbContext.SaveChangesAsync(ct);

            // Generate JWT access token
            var accessToken = jwtService.GenerateAccessToken(userId, user.IsAdmin);

            AuthCookieHelper.SetRefreshTokenCookie(httpContext, refreshTokenValue);

            return Results.Ok(new SeedUserResponse(
                userId.ToString(),
                user.Username,
                accessToken,
                refreshTokenValue));
        })
        .AllowAnonymous()
        .WithName("TestSeedUser")
        .WithTags("Test");
    }
}
