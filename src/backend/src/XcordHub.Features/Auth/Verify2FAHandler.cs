using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Auth;

public sealed record Verify2FARequest(string Code);

public sealed record Verify2FACommand(long UserId, string Code);

public sealed class Verify2FAHandler(HubDbContext dbContext)
    : IRequestHandler<Verify2FACommand, Result<bool>>, IValidatable<Verify2FACommand>
{
    public Error? Validate(Verify2FACommand request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return Error.Validation("VALIDATION_FAILED", "Verification code is required");

        if (request.Code.Length != 6)
            return Error.Validation("VALIDATION_FAILED", "Verification code must be 6 digits");

        if (!Regex.IsMatch(request.Code, @"^\d{6}$"))
            return Error.Validation("VALIDATION_FAILED", "Verification code must be numeric");

        return null;
    }

    public async Task<Result<bool>> Handle(Verify2FACommand request, CancellationToken cancellationToken)
    {
        var user = await dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user == null)
        {
            return Error.NotFound("USER_NOT_FOUND", "User not found");
        }

        if (string.IsNullOrEmpty(user.TwoFactorSecret))
        {
            return Error.Validation("2FA_NOT_SETUP", "Two-factor authentication is not set up");
        }

        // Validate TOTP code
        if (!ValidateTotpCode(user.TwoFactorSecret, request.Code))
        {
            return Error.Validation("INVALID_CODE", "Invalid verification code");
        }

        // Enable 2FA
        user.TwoFactorEnabled = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/auth/2fa/verify", async (
            [FromBody] Verify2FARequest request,
            ClaimsPrincipal user,
            Verify2FAHandler handler,
            CancellationToken ct) =>
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !long.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            var command = new Verify2FACommand(userId, request.Code);
            var result = await handler.ExecuteAsync(command, ct, _ => Results.NoContent());
            return result;
        })
        .RequireAuthorization(Policies.User)
        .Produces(204)
        .WithTags("Auth");
    }

    private static bool ValidateTotpCode(string base32Secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6)
        {
            return false;
        }

        var secretBytes = Base32Decode(base32Secret);
        var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timeStep = unixTime / 30;

        // Check current time window and ±1 window for clock skew
        for (long offset = -1; offset <= 1; offset++)
        {
            var counter = timeStep + offset;
            var expectedCode = GenerateTotpCode(secretBytes, counter);
            if (expectedCode == code)
            {
                return true;
            }
        }

        return false;
    }

    private static string GenerateTotpCode(byte[] secret, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counterBytes);

        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);

        var otp = binary % 1000000;
        return otp.ToString("D6");
    }

    private static byte[] Base32Decode(string base32)
    {
        const string base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        base32 = base32.ToUpperInvariant().TrimEnd('=');

        var numBytes = base32.Length * 5 / 8;
        var result = new byte[numBytes];

        var bitBuffer = 0;
        var bitsInBuffer = 0;
        var resultIndex = 0;

        foreach (var c in base32)
        {
            var value = base32Chars.IndexOf(c);
            if (value < 0)
            {
                continue;
            }

            bitBuffer = (bitBuffer << 5) | value;
            bitsInBuffer += 5;

            if (bitsInBuffer >= 8)
            {
                result[resultIndex++] = (byte)(bitBuffer >> (bitsInBuffer - 8));
                bitsInBuffer -= 8;
            }
        }

        return result;
    }
}
