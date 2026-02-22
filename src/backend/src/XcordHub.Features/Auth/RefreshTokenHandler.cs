using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Auth;

public sealed record RefreshTokenRequest;

public sealed record RefreshTokenResponse(string AccessToken, string RefreshToken);

public sealed class RefreshTokenHandler(
    HubDbContext dbContext,
    IJwtService jwtService,
    SnowflakeId snowflakeGenerator)
    : IRequestHandler<RefreshTokenRequest, Result<RefreshTokenResponse>>
{
    public Task<Result<RefreshTokenResponse>> Handle(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        // Note: The refresh token value will be passed from the endpoint via HandleWithToken
        return Task.FromResult<Result<RefreshTokenResponse>>(Error.Validation("INVALID_TOKEN", "Invalid or expired refresh token"));
    }

    public async Task<Result<RefreshTokenResponse>> HandleWithToken(string refreshTokenValue, CancellationToken cancellationToken)
    {
        // Hash the token
        var tokenHash = HashToken(refreshTokenValue);

        // Find the refresh token in database
        var refreshToken = await dbContext.RefreshTokens
            .Include(rt => rt.HubUser)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

        if (refreshToken == null)
        {
            return Error.Validation("INVALID_TOKEN", "Invalid or expired refresh token");
        }

        // Check if expired
        if (refreshToken.ExpiresAt < DateTimeOffset.UtcNow)
        {
            dbContext.RefreshTokens.Remove(refreshToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Error.Validation("INVALID_TOKEN", "Invalid or expired refresh token");
        }

        // Check if user account is disabled
        if (refreshToken.HubUser.IsDisabled)
        {
            return Error.Forbidden("ACCOUNT_DISABLED", "Account is disabled");
        }

        // Hard-delete old refresh token (rotation)
        dbContext.RefreshTokens.Remove(refreshToken);

        // Create new refresh token (30 days)
        var newRefreshTokenValue = GenerateRefreshToken();
        var newRefreshTokenHash = HashToken(newRefreshTokenValue);
        var now = DateTimeOffset.UtcNow;

        var newRefreshToken = new Entities.RefreshToken
        {
            Id = snowflakeGenerator.NextId(),
            TokenHash = newRefreshTokenHash,
            HubUserId = refreshToken.HubUserId,
            ExpiresAt = now.AddDays(30),
            CreatedAt = now
        };

        dbContext.RefreshTokens.Add(newRefreshToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Generate new JWT access token
        var accessToken = jwtService.GenerateAccessToken(
            refreshToken.HubUser.Id,
            refreshToken.HubUser.IsAdmin);

        return new RefreshTokenResponse(accessToken, newRefreshTokenValue);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/auth/refresh", async (
            HttpContext httpContext,
            RefreshTokenHandler handler,
            CancellationToken ct) =>
        {
            // Get refresh token from cookie
            if (!httpContext.Request.Cookies.TryGetValue("refresh_token", out var refreshTokenValue) ||
                string.IsNullOrWhiteSpace(refreshTokenValue))
            {
                return Results.Problem(
                    statusCode: 401,
                    title: "INVALID_TOKEN",
                    detail: "Invalid or expired refresh token");
            }

            var result = await handler.HandleWithToken(refreshTokenValue, ct);

            return result.Match(
                success =>
                {
                    // Set httpOnly cookie with new refresh token
                    httpContext.Response.Cookies.Append("refresh_token", success.RefreshToken, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = true,
                        SameSite = SameSiteMode.Strict,
                        Expires = DateTimeOffset.UtcNow.AddDays(30)
                    });

                    return Results.Ok(new
                    {
                        accessToken = success.AccessToken
                    });
                },
                error => Results.Problem(
                    statusCode: error.StatusCode,
                    title: error.Code,
                    detail: error.Message)
            );
        })
        .AllowAnonymous()
        .WithName("RefreshToken")
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
