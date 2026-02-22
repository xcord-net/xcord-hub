using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Auth;

public sealed record Enable2FACommand(long UserId);

public sealed record Enable2FAResponse(string Secret, string QrCodeUrl);

public sealed class Enable2FAHandler(HubDbContext dbContext)
    : IRequestHandler<Enable2FACommand, Result<Enable2FAResponse>>
{
    public async Task<Result<Enable2FAResponse>> Handle(Enable2FACommand request, CancellationToken cancellationToken)
    {
        var user = await dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user == null)
        {
            return Error.NotFound("USER_NOT_FOUND", "User not found");
        }

        if (user.TwoFactorEnabled)
        {
            return Error.Validation("2FA_ALREADY_ENABLED", "Two-factor authentication is already enabled");
        }

        // Generate TOTP secret (base32-encoded 160-bit secret)
        var secret = GenerateBase32Secret();
        user.TwoFactorSecret = secret;

        // Generate QR code URL (otpauth://totp/...)
        var qrCodeUrl = $"otpauth://totp/XcordHub:{user.Username}?secret={secret}&issuer=XcordHub";

        await dbContext.SaveChangesAsync(cancellationToken);

        return new Enable2FAResponse(secret, qrCodeUrl);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/auth/2fa/enable", async (
            ClaimsPrincipal user,
            Enable2FAHandler handler,
            CancellationToken ct) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !long.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            var command = new Enable2FACommand(userId);
            var result = await handler.ExecuteAsync(command, ct);
            return result;
        })
        .RequireAuthorization(Policies.User)
        .Produces<Enable2FAResponse>(200)
        .WithTags("Auth");
    }

    private static string GenerateBase32Secret()
    {
        const string base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var randomBytes = new byte[20]; // 160 bits
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);

        var result = new char[32]; // 160 bits / 5 bits per char = 32 chars
        for (int i = 0; i < 32; i++)
        {
            var bitIndex = i * 5;
            var byteIndex = bitIndex / 8;
            var bitOffset = bitIndex % 8;

            int value;
            if (bitOffset <= 3)
            {
                value = (randomBytes[byteIndex] >> (3 - bitOffset)) & 0x1F;
            }
            else
            {
                value = ((randomBytes[byteIndex] << (bitOffset - 3)) & 0x1F);
                if (byteIndex + 1 < randomBytes.Length)
                {
                    value |= (randomBytes[byteIndex + 1] >> (11 - bitOffset)) & 0x1F;
                }
            }

            result[i] = base32Chars[value];
        }

        return new string(result);
    }
}
