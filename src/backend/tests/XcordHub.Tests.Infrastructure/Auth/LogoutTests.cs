using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub.Entities;
using XcordHub.Features.Auth;

namespace XcordHub.Tests.Infrastructure.Auth;

/// <summary>
/// Integration tests for LogoutHandler -- specifically the HandleWithToken
/// entry point that revokes a refresh token.
///
/// User IDs reserved: 1_463_000_000 – 1_463_000_099
/// </summary>
[Collection("AuthIntegration")]
[Trait("Category", "Integration")]
public sealed class LogoutTests : AuthTestsBase
{
    private const long UserIdBase = 1_463_000_000L;

    public LogoutTests(AuthIntegrationFixture fixture)
        : base(fixture, "xcordhub_logout_test") { }

    private async Task<(HubUser user, string rawToken, RefreshToken stored)> SeedRefreshTokenAsync(
        HubDbContext db,
        long userId,
        string suffix)
    {
        var (user, _, _) = await SeedUserAsync(db, userId, suffix);
        var rawToken = TokenHelper.GenerateToken();
        var stored = new RefreshToken
        {
            Id = new SnowflakeIdGenerator(463).NextId(),
            HubUserId = user.Id,
            TokenHash = TokenHelper.HashToken(rawToken),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        };
        db.RefreshTokens.Add(stored);
        await db.SaveChangesAsync();
        return (user, rawToken, stored);
    }

    [Fact]
    public async Task Logout_ValidRefreshToken_DeletesTokenRow()
    {
        await using var db = CreateDbContext();
        var (user, rawToken, stored) = await SeedRefreshTokenAsync(db, UserIdBase + 1, "valid");

        var handler = new LogoutHandler(db);
        var result = await handler.HandleWithToken(rawToken, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();

        await using var verifyDb = CreateDbContext();
        var exists = await verifyDb.RefreshTokens.AnyAsync(rt => rt.Id == stored.Id);
        exists.Should().BeFalse(
            "logout must hard-delete the refresh-token row so subsequent refresh calls fail");

        var anyForUser = await verifyDb.RefreshTokens.AnyAsync(rt => rt.HubUserId == user.Id);
        anyForUser.Should().BeFalse(
            "the user should have no remaining refresh tokens after logout of their only session");
    }

    [Fact]
    public async Task Logout_UnknownToken_StillReturnsSuccess()
    {
        await using var db = CreateDbContext();

        var handler = new LogoutHandler(db);
        var bogus = TokenHelper.GenerateToken();
        var result = await handler.HandleWithToken(bogus, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(
            "logout must be idempotent - already-revoked or unknown tokens are not an error");
    }

    [Fact]
    public async Task Logout_OnlyRemovesMatchingToken_LeavesOtherSessionsAlone()
    {
        await using var db = CreateDbContext();
        var (user, sessionA, _) = await SeedRefreshTokenAsync(db, UserIdBase + 2, "multi");

        // Add a second refresh token simulating a second logged-in device/session.
        // Different worker ID from SeedRefreshTokenAsync (463) so a same-millisecond
        // call can't collide on a fresh-generator sequence-zero ID.
        var sessionB = TokenHelper.GenerateToken();
        var sessionBStored = new RefreshToken
        {
            Id = new SnowflakeIdGenerator(464).NextId(),
            HubUserId = user.Id,
            TokenHash = TokenHelper.HashToken(sessionB),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        };
        db.RefreshTokens.Add(sessionBStored);
        await db.SaveChangesAsync();

        // Log out only sessionA.
        var handler = new LogoutHandler(db);
        await handler.HandleWithToken(sessionA, CancellationToken.None);

        await using var verifyDb = CreateDbContext();
        var remaining = await verifyDb.RefreshTokens
            .Where(rt => rt.HubUserId == user.Id)
            .ToListAsync();

        remaining.Should().HaveCount(1,
            "logout must only revoke the matching session, not all sessions for the user");
        remaining[0].Id.Should().Be(sessionBStored.Id,
            "the surviving session must be the one whose token was NOT presented to logout");
    }
}
