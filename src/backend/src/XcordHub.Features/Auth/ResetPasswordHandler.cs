using System.Text.RegularExpressions;
using BCrypt.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Auth;

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record ResetPasswordCommand(string Token, string NewPassword);

public sealed class ResetPasswordHandler(HubDbContext dbContext)
    : IRequestHandler<ResetPasswordCommand, Result<bool>>, IValidatable<ResetPasswordCommand>
{
    public Error? Validate(ResetPasswordCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return Error.Validation("VALIDATION_FAILED", "Reset token is required");

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

    public async Task<Result<bool>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var tokenHash = TokenHelper.HashToken(request.Token);
        var now = DateTimeOffset.UtcNow;

        var resetToken = await dbContext.PasswordResetTokens
            .Include(t => t.HubUser)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (resetToken == null)
        {
            return Error.Validation("INVALID_TOKEN", "Invalid or expired reset token");
        }

        if (resetToken.IsUsed)
        {
            return Error.Validation("TOKEN_USED", "Reset token has already been used");
        }

        if (resetToken.ExpiresAt < now)
        {
            return Error.Validation("TOKEN_EXPIRED", "Reset token has expired");
        }

        // Update password
        resetToken.HubUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, 12);

        // Mark token as used
        resetToken.IsUsed = true;

        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/auth/reset-password", async (
            [FromBody] ResetPasswordRequest request,
            ResetPasswordHandler handler,
            CancellationToken ct) =>
        {
            var command = new ResetPasswordCommand(request.Token, request.NewPassword);
            var result = await handler.ExecuteAsync(command, ct, _ => Results.NoContent());
            return result;
        })
        .Produces(204)
        .WithTags("Auth");
    }

}
