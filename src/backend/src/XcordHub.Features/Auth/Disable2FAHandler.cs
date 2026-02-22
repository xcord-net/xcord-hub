using System.Security.Claims;
using BCrypt.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Auth;

public sealed record Disable2FARequest(string Password);

public sealed record Disable2FACommand(long UserId, string Password);

public sealed class Disable2FAHandler(HubDbContext dbContext)
    : IRequestHandler<Disable2FACommand, Result<bool>>, IValidatable<Disable2FACommand>
{
    public Error? Validate(Disable2FACommand request)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
            return Error.Validation("VALIDATION_FAILED", "Password is required");

        return null;
    }

    public async Task<Result<bool>> Handle(Disable2FACommand request, CancellationToken cancellationToken)
    {
        var user = await dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user == null)
        {
            return Error.NotFound("USER_NOT_FOUND", "User not found");
        }

        // Verify password before disabling 2FA
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Error.Validation("INVALID_PASSWORD", "Invalid password");
        }

        user.TwoFactorEnabled = false;
        user.TwoFactorSecret = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/auth/2fa/disable", async (
            [FromBody] Disable2FARequest request,
            ClaimsPrincipal user,
            Disable2FAHandler handler,
            CancellationToken ct) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !long.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            var command = new Disable2FACommand(userId, request.Password);
            var result = await handler.ExecuteAsync(command, ct, _ => Results.NoContent());
            return result;
        })
        .RequireAuthorization(Policies.User)
        .Produces(204)
        .WithTags("Auth");
    }
}
