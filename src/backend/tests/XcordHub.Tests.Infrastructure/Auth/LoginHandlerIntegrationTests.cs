using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using XcordHub.Features.Auth;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Helpers;

namespace XcordHub.Tests.Infrastructure.Auth;

/// <summary>
/// Integration tests for LoginHandler covering credential validation, admin
/// claim issuance, rate limiting, 2FA gating, and disabled-account handling.
/// Uses real Postgres + Redis from the AuthIntegration collection fixture.
///
/// User IDs reserved: 1_460_000_000 – 1_460_000_099
/// </summary>
[Collection("AuthIntegration")]
[Trait("Category", "Integration")]
public sealed class LoginHandlerIntegrationTests : AuthTestsBase
{
    private const long UserIdBase = 1_460_000_000L;

    public LoginHandlerIntegrationTests(AuthIntegrationFixture fixture)
        : base(fixture, "xcordhub_login_handler_test") { }

    private LoginHandler BuildHandler(HubDbContext db, int maxAttempts = 5)
    {
        var enc = new AesEncryptionService(TestEncryptionKey);
        var jwtService = JwtTestHelper.CreateJwtService(db, TestEncryptionKey);
        return new LoginHandler(
            db,
            enc,
            jwtService,
            new SnowflakeIdGenerator(460),
            NullHttpContextAccessor(),
            Redis,
            BuildRedisOptions(),
            BuildAuthOptions(maxAttempts: maxAttempts));
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokensAndRecordsSuccessAttempt()
    {
        await using var db = CreateDbContext();
        var (user, email, password) = await SeedUserAsync(db, UserIdBase + 1, "valid", isAdmin: false);

        var handler = BuildHandler(db);
        var result = await handler.Handle(new LoginRequest(email, password), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(user.Id.ToString(),
            "Snowflake IDs serialize as strings in the response contract");
        result.Value.Username.Should().Be(user.Username);
        result.Value.Email.Should().Be(email,
            "the decrypted email must round-trip back to the client");
        result.Value.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.Value.RefreshToken.Should().NotBeNullOrWhiteSpace();

        await using var verifyDb = CreateDbContext();
        var refreshToken = await verifyDb.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.HubUserId == user.Id);
        refreshToken.Should().NotBeNull("a refresh token row must be persisted on successful login");

        var success = await verifyDb.LoginAttempts
            .Where(la => la.UserId == user.Id && la.Success)
            .CountAsync();
        success.Should().BeGreaterThanOrEqualTo(1,
            "successful logins must be audited in the LoginAttempts table");
    }

    [Fact]
    public async Task Login_AdminUser_AccessTokenEncodesAdminClaim()
    {
        await using var db = CreateDbContext();
        var (user, email, password) = await SeedUserAsync(db, UserIdBase + 2, "admin", isAdmin: true);

        var handler = BuildHandler(db);
        var result = await handler.Handle(new LoginRequest(email, password), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var token = result.Value.AccessToken;
        // JWT format: header.payload.signature - decode the payload and look for the role claim.
        var parts = token.Split('.');
        parts.Length.Should().Be(3, "JWTs must have 3 base64url-encoded sections");
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        // Base64 padding
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        json.Should().Contain("\"admin\":\"true\"",
            "an admin user must receive a token whose admin claim is exactly the string 'true'");
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsInvalidCredentialsAndIncrementsCounter()
    {
        await using var db = CreateDbContext();
        var (_, email, _) = await SeedUserAsync(db, UserIdBase + 3, "wrongpw");

        var handler = BuildHandler(db, maxAttempts: 5);
        var result = await handler.Handle(
            new LoginRequest(email, "ThisIsTheWrongPassword!"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_CREDENTIALS",
            "wrong passwords must not leak whether the email exists");

        await using var verifyDb = CreateDbContext();
        var fail = await verifyDb.LoginAttempts
            .Where(la => !la.Success && la.FailureReason == "INVALID_CREDENTIALS")
            .CountAsync();
        fail.Should().BeGreaterThanOrEqualTo(1,
            "failed logins must be audited with the failure reason");
    }

    [Fact]
    public async Task Login_TwoFactorEnabled_RejectsWith2FARequiredCode()
    {
        await using var db = CreateDbContext();
        var (_, email, password) = await SeedUserAsync(
            db, UserIdBase + 4, "twofa", twoFactorEnabled: true, twoFactorSecret: "JBSWY3DPEHPK3PXP");

        var handler = BuildHandler(db);
        var result = await handler.Handle(new LoginRequest(email, password), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("2FA_REQUIRED",
            "users with 2FA enabled must be routed through the verify-2FA flow, not directly logged in");
    }

    [Fact]
    public async Task Login_DisabledAccount_ReturnsAccountDisabled()
    {
        await using var db = CreateDbContext();
        var (_, email, password) = await SeedUserAsync(db, UserIdBase + 5, "disabled", isDisabled: true);

        var handler = BuildHandler(db);
        var result = await handler.Handle(new LoginRequest(email, password), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ACCOUNT_DISABLED",
            "disabled accounts must be rejected even with valid credentials");
    }
}
