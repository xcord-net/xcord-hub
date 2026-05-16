using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub.Entities;
using XcordHub.Features.Auth;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Helpers;

namespace XcordHub.Tests.Infrastructure.Auth;

/// <summary>
/// Integration tests for RefreshTokenHandler covering the rotation contract:
/// valid refresh issues new pair + revokes old, expired token is rejected,
/// replayed (already-rotated) token is rejected, and disabled-account
/// refresh attempts are forbidden.
///
/// User IDs reserved: 1_461_000_000 – 1_461_000_099
/// </summary>
[Collection("AuthIntegration")]
[Trait("Category", "Integration")]
public sealed class RefreshTokenTests : AuthTestsBase
{
    private const long UserIdBase = 1_461_000_000L;

    public RefreshTokenTests(AuthIntegrationFixture fixture)
        : base(fixture, "xcordhub_refresh_token_test") { }

    private RefreshTokenHandler BuildHandler(HubDbContext db) =>
        new RefreshTokenHandler(
            db,
            JwtTestHelper.CreateJwtService(db, TestEncryptionKey),
            new SnowflakeIdGenerator(461),
            BuildAuthOptions());

    private async Task<(HubUser user, string rawToken, RefreshToken stored)> SeedRefreshTokenAsync(
        HubDbContext db,
        long userId,
        string suffix,
        DateTimeOffset? expiresAt = null,
        bool isDisabled = false)
    {
        var (user, _, _) = await SeedUserAsync(db, userId, suffix, isDisabled: isDisabled);
        var rawToken = TokenHelper.GenerateToken();
        var stored = new RefreshToken
        {
            Id = new SnowflakeIdGenerator(461).NextId(),
            HubUserId = user.Id,
            TokenHash = TokenHelper.HashToken(rawToken),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(30)
        };
        db.RefreshTokens.Add(stored);
        await db.SaveChangesAsync();
        return (user, rawToken, stored);
    }

    [Fact]
    public async Task Refresh_ValidToken_RotatesAndReturnsNewPair()
    {
        await using var db = CreateDbContext();
        var (user, rawToken, stored) = await SeedRefreshTokenAsync(db, UserIdBase + 1, "rotate");

        var handler = BuildHandler(db);
        var result = await handler.HandleWithToken(rawToken, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.Value.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.Value.RefreshToken.Should().NotBe(rawToken,
            "rotation must return a fresh refresh token, never the same value");

        await using var verifyDb = CreateDbContext();
        var oldExists = await verifyDb.RefreshTokens.AnyAsync(rt => rt.Id == stored.Id);
        oldExists.Should().BeFalse(
            "the old refresh-token row must be hard-deleted on rotation");

        var newTokenCount = await verifyDb.RefreshTokens
            .Where(rt => rt.HubUserId == user.Id)
            .CountAsync();
        newTokenCount.Should().Be(1,
            "exactly one refresh token must exist for the user after rotation");
    }

    [Fact]
    public async Task Refresh_ExpiredToken_RejectsAndCleansUpRow()
    {
        await using var db = CreateDbContext();
        var (user, rawToken, stored) = await SeedRefreshTokenAsync(
            db, UserIdBase + 2, "expired",
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var handler = BuildHandler(db);
        var result = await handler.HandleWithToken(rawToken, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_TOKEN",
            "an expired refresh token must be rejected with the generic INVALID_TOKEN error");

        await using var verifyDb = CreateDbContext();
        var stillExists = await verifyDb.RefreshTokens.AnyAsync(rt => rt.Id == stored.Id);
        stillExists.Should().BeFalse(
            "expired refresh tokens must be cleaned up so they cannot be replayed later");
    }

    [Fact]
    public async Task Refresh_ReplayedAfterRotation_RejectsAsInvalid()
    {
        await using var db = CreateDbContext();
        var (_, rawToken, _) = await SeedRefreshTokenAsync(db, UserIdBase + 3, "replay");

        var handler = BuildHandler(db);

        // First use - rotates successfully.
        var first = await handler.HandleWithToken(rawToken, CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        // Replay the original (now-rotated) token.
        await using var replayDb = CreateDbContext();
        var replayHandler = new RefreshTokenHandler(
            replayDb,
            JwtTestHelper.CreateJwtService(replayDb, TestEncryptionKey),
            new SnowflakeIdGenerator(461),
            BuildAuthOptions());
        var replay = await replayHandler.HandleWithToken(rawToken, CancellationToken.None);

        replay.IsFailure.Should().BeTrue();
        replay.Error!.Code.Should().Be("INVALID_TOKEN",
            "rotation must invalidate the original token so replays fail");
    }

    [Fact]
    public async Task Refresh_UnknownToken_ReturnsInvalidToken()
    {
        await using var db = CreateDbContext();

        var handler = BuildHandler(db);
        var bogus = TokenHelper.GenerateToken();
        var result = await handler.HandleWithToken(bogus, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_TOKEN",
            "a token that has no DB row must be rejected as invalid");
    }

    [Fact]
    public async Task Refresh_DisabledAccount_ReturnsAccountDisabled()
    {
        await using var db = CreateDbContext();
        var (_, rawToken, _) = await SeedRefreshTokenAsync(
            db, UserIdBase + 4, "disabled", isDisabled: true);

        var handler = BuildHandler(db);
        var result = await handler.HandleWithToken(rawToken, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ACCOUNT_DISABLED",
            "a disabled user's refresh token must be rejected even if it is otherwise valid");
    }
}
