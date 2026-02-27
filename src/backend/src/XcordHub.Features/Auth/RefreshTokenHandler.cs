using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Auth;

public sealed record RefreshTokenResponse(string AccessToken, string RefreshToken);

public sealed record RefreshTokenApiResponse(string AccessToken);

public sealed class RefreshTokenHandler(
    HubDbContext dbContext,
    IJwtService jwtService,
    SnowflakeId snowflakeGenerator)
    : IEndpoint
{
    public async Task<Result<RefreshTokenResponse>> HandleWithToken(string refreshTokenValue, CancellationToken cancellationToken)
    {
        // Hash the token
        var tokenHash = TokenHelper.HashToken(refreshTokenValue);

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
        var newRefreshTokenValue = TokenHelper.GenerateToken();
        var newRefreshTokenHash = TokenHelper.HashToken(newRefreshTokenValue);
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
                    AuthCookieHelper.SetRefreshTokenCookie(httpContext, success.RefreshToken);

                    return Results.Ok(new RefreshTokenApiResponse(success.AccessToken));
                },
                error => Results.Problem(
                    statusCode: error.StatusCode,
                    title: error.Code,
                    detail: error.Message)
            );
        })
        .AllowAnonymous()
        .Produces<RefreshTokenApiResponse>(200)
        .WithName("RefreshToken")
        .WithTags("Auth");
    }

}
