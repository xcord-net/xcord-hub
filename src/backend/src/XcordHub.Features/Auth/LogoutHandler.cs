using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Auth;

public sealed record LogoutRequest;

public sealed class LogoutHandler(HubDbContext dbContext)
    : IRequestHandler<LogoutRequest, Result<bool>>
{
    public Task<Result<bool>> Handle(LogoutRequest request, CancellationToken cancellationToken)
    {
        // Note: The refresh token value will be passed from the endpoint via HandleWithToken
        return Task.FromResult<Result<bool>>(true);
    }

    public async Task<Result<bool>> HandleWithToken(string refreshTokenValue, CancellationToken cancellationToken)
    {
        // Hash the token
        var tokenHash = HashToken(refreshTokenValue);

        // Find and delete the refresh token
        var refreshToken = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

        if (refreshToken != null)
        {
            dbContext.RefreshTokens.Remove(refreshToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/auth/logout", async (
            HttpContext httpContext,
            LogoutHandler handler,
            CancellationToken ct) =>
        {
            // Get refresh token from cookie
            if (httpContext.Request.Cookies.TryGetValue("refresh_token", out var refreshTokenValue) &&
                !string.IsNullOrWhiteSpace(refreshTokenValue))
            {
                await handler.HandleWithToken(refreshTokenValue, ct);
            }

            // Delete the cookie
            httpContext.Response.Cookies.Delete("refresh_token");

            return Results.Ok(new { success = true });
        })
        .RequireAuthorization(Policies.User)
        .WithName("Logout")
        .WithTags("Auth");
    }

    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hashBytes);
    }
}
