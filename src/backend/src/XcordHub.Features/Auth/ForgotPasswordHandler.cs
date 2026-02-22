using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Auth;

public sealed record ForgotPasswordRequest(string Email);

public sealed record ForgotPasswordCommand(string Email);

public sealed class ForgotPasswordHandler(
    HubDbContext dbContext,
    IEncryptionService encryptionService,
    IEmailService emailService,
    IOptions<EmailOptions> emailOptions,
    SnowflakeId snowflakeGenerator,
    ILogger<ForgotPasswordHandler> logger)
    : IRequestHandler<ForgotPasswordCommand, Result<bool>>, IValidatable<ForgotPasswordCommand>
{
    public Error? Validate(ForgotPasswordCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return Error.Validation("VALIDATION_FAILED", "Email is required");

        if (!Regex.IsMatch(request.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return Error.Validation("VALIDATION_FAILED", "Invalid email format");

        return null;
    }

    public async Task<Result<bool>> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        // Find user by EmailHash
        var emailHash = encryptionService.ComputeHmac(request.Email.ToLowerInvariant());
        var user = await dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.EmailHash == emailHash, cancellationToken);

        // Always return success to prevent email enumeration
        if (user == null || user.IsDisabled)
        {
            logger.LogWarning("Password reset requested for non-existent or disabled email");
            return true;
        }

        // Generate reset token
        var resetTokenValue = TokenHelper.GenerateToken();
        var resetTokenHash = TokenHelper.HashToken(resetTokenValue);
        var now = DateTimeOffset.UtcNow;

        var resetToken = new PasswordResetToken
        {
            Id = snowflakeGenerator.NextId(),
            TokenHash = resetTokenHash,
            HubUserId = user.Id,
            ExpiresAt = now.AddHours(1),
            CreatedAt = now,
            IsUsed = false
        };

        dbContext.PasswordResetTokens.Add(resetToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Decrypt stored email and send reset link
        var plaintextEmail = encryptionService.Decrypt(user.Email);
        var resetUrl = BuildResetUrl(emailOptions.Value.HubBaseUrl, resetTokenValue);
        var emailBody = BuildResetEmailBody(user.DisplayName, resetUrl);

        await emailService.SendAsync(plaintextEmail, "Reset your Xcord password", emailBody);

        logger.LogInformation("Password reset email sent for user {UserId}", user.Id);

        return true;
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/auth/forgot-password", async (
            [FromBody] ForgotPasswordRequest request,
            ForgotPasswordHandler handler,
            CancellationToken ct) =>
        {
            var command = new ForgotPasswordCommand(request.Email);
            var result = await handler.ExecuteAsync(command, ct, _ => Results.NoContent());
            return result;
        })
        .WithTags("Auth");
    }

    private static string BuildResetUrl(string hubBaseUrl, string token)
    {
        var baseUrl = hubBaseUrl.TrimEnd('/');
        return $"{baseUrl}/reset-password?token={HttpUtility.UrlEncode(token)}";
    }

    private static string BuildResetEmailBody(string displayName, string resetUrl)
    {
        return $"""
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            </head>
            <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background-color: #313338; margin: 0; padding: 40px 0;">
              <table width="100%" cellpadding="0" cellspacing="0" style="max-width: 480px; margin: 0 auto;">
                <tr>
                  <td style="background-color: #2b2d31; border-radius: 8px; padding: 40px;">
                    <h1 style="color: #ffffff; font-size: 22px; margin: 0 0 8px 0;">xcord</h1>
                    <h2 style="color: #dbdee1; font-size: 18px; margin: 0 0 24px 0;">Reset your password</h2>
                    <p style="color: #b5bac1; font-size: 14px; line-height: 1.6; margin: 0 0 24px 0;">
                      Hi {displayName},<br /><br />
                      We received a request to reset the password for your Xcord account.
                      Click the button below to choose a new password. This link expires in <strong style="color: #dbdee1;">1 hour</strong>.
                    </p>
                    <a href="{resetUrl}"
                       style="display: inline-block; padding: 12px 24px; background-color: #5865f2; color: #ffffff;
                              text-decoration: none; border-radius: 4px; font-size: 14px; font-weight: 600;">
                      Reset Password
                    </a>
                    <p style="color: #b5bac1; font-size: 12px; line-height: 1.6; margin: 24px 0 0 0;">
                      If you didn't request a password reset, you can safely ignore this email.
                      Your password will not be changed.
                    </p>
                    <hr style="border: none; border-top: 1px solid #3f4147; margin: 24px 0;" />
                    <p style="color: #6d6f78; font-size: 11px; margin: 0;">
                      &copy; Xcord. All rights reserved.
                    </p>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }
}
