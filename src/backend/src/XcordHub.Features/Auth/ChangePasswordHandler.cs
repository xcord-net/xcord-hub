using System.Security.Claims;
using System.Text.RegularExpressions;
using BCrypt.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Auth;

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword
);

public sealed record ChangePasswordCommand(
    long UserId,
    string CurrentPassword,
    string NewPassword
);

public sealed class ChangePasswordHandler(HubDbContext dbContext)
    : IRequestHandler<ChangePasswordCommand, Result<bool>>, IValidatable<ChangePasswordCommand>
{
    public Error? Validate(ChangePasswordCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            return Error.Validation("VALIDATION_FAILED", "Current password is required");

        if (string.IsNullOrWhiteSpace(request.NewPassword))
            return Error.Validation("VALIDATION_FAILED", "New password is required");

        if (request.NewPassword.Length < 8)
            return Error.Validation("VALIDATION_FAILED", "Password must be at least 8 characters");

        if (!Regex.IsMatch(request.NewPassword, @"[A-Z]"))
            return Error.Validation("VALIDATION_FAILED", "Password must contain at least one uppercase letter");

        if (!Regex.IsMatch(request.NewPassword, @"[a-z]"))
            return Error.Validation("VALIDATION_FAILED", "Password must contain at least one lowercase letter");

        if (!Regex.IsMatch(request.NewPassword, @"[0-9]"))
            return Error.Validation("VALIDATION_FAILED", "Password must contain at least one number");

        return null;
    }

    public async Task<Result<bool>> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user == null)
        {
            return Error.NotFound("USER_NOT_FOUND", "User not found");
        }

        // Verify current password
        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Error.Validation("INVALID_PASSWORD", "Current password is incorrect");
        }

        // Hash new password
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, 12);

        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/auth/change-password", async (
            [FromBody] ChangePasswordRequest request,
            ClaimsPrincipal user,
            ChangePasswordHandler handler,
            CancellationToken ct) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !long.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            var command = new ChangePasswordCommand(
                userId,
                request.CurrentPassword,
                request.NewPassword
            );

            var result = await handler.ExecuteAsync(command, ct, _ => Results.NoContent());
            return result;
        })
        .RequireAuthorization(Policies.User)
        .WithTags("Auth");
    }
}
