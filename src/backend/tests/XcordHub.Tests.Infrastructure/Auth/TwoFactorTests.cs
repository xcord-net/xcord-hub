using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using XcordHub.Features.Auth;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Tests.Infrastructure.Auth;

/// <summary>
/// Integration tests for the Enable2FA -> Verify2FA -> login-2FA-required flow.
///
/// User IDs reserved: 1_462_000_000 – 1_462_000_099
/// </summary>
[Collection("AuthIntegration")]
[Trait("Category", "Integration")]
public sealed class TwoFactorTests : AuthTestsBase
{
    private const long UserIdBase = 1_462_000_000L;

    public TwoFactorTests(AuthIntegrationFixture fixture)
        : base(fixture, "xcordhub_two_factor_test") { }

    [Fact]
    public async Task Enable2FA_FreshUser_PersistsSecretAndReturnsQrUrl()
    {
        await using var db = CreateDbContext();
        var (user, _, _) = await SeedUserAsync(db, UserIdBase + 1, "enable");

        var handler = new Enable2FAHandler(db);
        var result = await handler.Handle(new Enable2FACommand(user.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Secret.Should().NotBeNullOrWhiteSpace();
        result.Value.Secret.Length.Should().Be(32,
            "the TOTP secret is encoded as 32 base32 characters (160 bits)");
        result.Value.QrCodeUrl.Should().StartWith("otpauth://totp/XcordHub:",
            "the QR-code URL must follow the otpauth:// scheme so authenticator apps can scan it");
        result.Value.QrCodeUrl.Should().Contain($"secret={result.Value.Secret}");

        await using var verifyDb = CreateDbContext();
        var refreshed = await verifyDb.HubUsers.FirstAsync(u => u.Id == user.Id);
        refreshed.TwoFactorSecret.Should().Be(result.Value.Secret,
            "the secret must be persisted so verify can validate codes against it");
        refreshed.TwoFactorEnabled.Should().BeFalse(
            "TwoFactorEnabled is only flipped to true once the user verifies a TOTP code");
    }

    [Fact]
    public async Task Enable2FA_AlreadyEnabled_ReturnsValidationError()
    {
        await using var db = CreateDbContext();
        var (user, _, _) = await SeedUserAsync(
            db, UserIdBase + 2, "already", twoFactorEnabled: true, twoFactorSecret: "JBSWY3DPEHPK3PXP");

        var handler = new Enable2FAHandler(db);
        var result = await handler.Handle(new Enable2FACommand(user.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("2FA_ALREADY_ENABLED",
            "the handler must refuse to overwrite an active 2FA secret");
    }

    [Fact]
    public async Task Enable2FA_UnknownUser_ReturnsNotFound()
    {
        await using var db = CreateDbContext();
        var handler = new Enable2FAHandler(db);

        var result = await handler.Handle(
            new Enable2FACommand(UserId: 999_888_777_666L), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("USER_NOT_FOUND");
    }

    [Fact]
    public async Task Verify2FA_WithCurrentTotpCode_EnablesTwoFactor()
    {
        await using var db = CreateDbContext();
        var (user, _, _) = await SeedUserAsync(db, UserIdBase + 3, "verify");

        // Seed a known TOTP secret via Enable2FA so we can compute the current code.
        var enable = await new Enable2FAHandler(db).Handle(
            new Enable2FACommand(user.Id), CancellationToken.None);
        enable.IsSuccess.Should().BeTrue();
        var code = ComputeCurrentTotpCode(enable.Value.Secret);

        var verifyHandler = new Verify2FAHandler(db);
        var result = await verifyHandler.Handle(
            new Verify2FACommand(user.Id, code), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("the freshly-computed TOTP code must validate");
        result.Value.Should().BeTrue();

        await using var verifyDb = CreateDbContext();
        var refreshed = await verifyDb.HubUsers.FirstAsync(u => u.Id == user.Id);
        refreshed.TwoFactorEnabled.Should().BeTrue(
            "a successful verify must flip TwoFactorEnabled = true");
    }

    [Fact]
    public async Task Verify2FA_WithWrongCode_ReturnsInvalidCode()
    {
        await using var db = CreateDbContext();
        var (user, _, _) = await SeedUserAsync(db, UserIdBase + 4, "badcode");
        var enable = await new Enable2FAHandler(db).Handle(
            new Enable2FACommand(user.Id), CancellationToken.None);
        enable.IsSuccess.Should().BeTrue();

        var verifyHandler = new Verify2FAHandler(db);
        var result = await verifyHandler.Handle(
            new Verify2FACommand(user.Id, "000000"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_CODE",
            "a deliberately-wrong code must not enable 2FA");

        await using var verifyDb = CreateDbContext();
        var refreshed = await verifyDb.HubUsers.FirstAsync(u => u.Id == user.Id);
        refreshed.TwoFactorEnabled.Should().BeFalse(
            "TwoFactorEnabled must remain false after a failed verify");
    }

    [Fact]
    public void Verify2FA_MalformedCode_ValidateRejects()
    {
        using var db = CreateDbContext();
        var handler = new Verify2FAHandler(db);

        var tooShort = handler.Validate(new Verify2FACommand(UserId: UserIdBase + 5, "12345"));
        var nonNumeric = handler.Validate(new Verify2FACommand(UserId: UserIdBase + 5, "abcdef"));

        tooShort.Should().NotBeNull();
        tooShort!.Code.Should().Be("VALIDATION_FAILED");
        nonNumeric.Should().NotBeNull();
        nonNumeric!.Code.Should().Be("VALIDATION_FAILED",
            "non-numeric codes must be rejected at validation time");
    }

    /// <summary>
    /// Reproduces the TOTP code that Verify2FAHandler will accept for the
    /// current 30-second window. Mirrors the algorithm in Verify2FAHandler.
    /// </summary>
    private static string ComputeCurrentTotpCode(string base32Secret)
    {
        var secret = Base32Decode(base32Secret);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);
        return (binary % 1000000).ToString("D6");
    }

    private static byte[] Base32Decode(string base32)
    {
        const string base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        base32 = base32.ToUpperInvariant().TrimEnd('=');
        var numBytes = base32.Length * 5 / 8;
        var result = new byte[numBytes];
        var bitBuffer = 0;
        var bitsInBuffer = 0;
        var idx = 0;
        foreach (var c in base32)
        {
            var value = base32Chars.IndexOf(c);
            if (value < 0) continue;
            bitBuffer = (bitBuffer << 5) | value;
            bitsInBuffer += 5;
            if (bitsInBuffer >= 8)
            {
                result[idx++] = (byte)(bitBuffer >> (bitsInBuffer - 8));
                bitsInBuffer -= 8;
            }
        }
        return result;
    }
}
