using BCrypt.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using XcordHub.Entities;
using XcordHub.Features.Auth;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Infrastructure.Fixtures;
using Xunit;

namespace XcordHub.Tests.Infrastructure;

/// <summary>
/// Integration tests for UserRegistrationService.
/// Verifies user creation, duplicate detection, password hashing, and
/// the contract that the service adds entities to the context without saving.
///
/// ID ranges reserved for this class:
///   User IDs: 1_249_000_000 – 1_249_000_099  (worker ID 249)
/// </summary>
[Collection("SharedPostgres")]
[Trait("Category", "Integration")]
public sealed class UserRegistrationServiceTests
{
    private readonly string _connectionString;

    private const string TestEncryptionKey = "user-reg-svc-tests-encryption-key-256-bits-minimum-ok!";
    private const long UserIdBase = 1_249_000_000L;

    public UserRegistrationServiceTests(SharedPostgresFixture fixture)
    {
        _connectionString = fixture.CreateDatabaseAsync("xcordhub_user_reg_svc", TestEncryptionKey).GetAwaiter().GetResult();
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private HubDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
    }

    private static UserRegistrationService CreateService(HubDbContext db) =>
        new UserRegistrationService(
            db,
            new NoOpCaptchaService(),
            new AesEncryptionService(TestEncryptionKey),
            new JwtService("test-issuer", "test-audience", "test-secret-key-must-be-at-least-32-characters-long!", 60),
            new SnowflakeIdGenerator(249),
            Options.Create(new AuthOptions { BcryptWorkFactor = 4 }));

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Register_ValidInput_CreatesUserAndTokens()
    {
        // Arrange
        await using var db = CreateDbContext();
        var service = CreateService(db);

        // Act
        var result = await service.RegisterAsync(
            "newuser_valid",
            "New User",
            "newuser_valid@test.invalid",
            "SecurePass1!",
            null,
            null,
            CancellationToken.None);

        // The service does not call SaveChanges itself — the caller must do so
        await db.SaveChangesAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().NotBeNullOrEmpty();
        result.Value.RefreshToken.Should().NotBeNullOrEmpty();

        // Verify via a fresh context to confirm data reached the database
        await using var verifyDb = CreateDbContext();
        var user = await verifyDb.HubUsers
            .FirstOrDefaultAsync(u => u.Username == "newuser_valid");
        user.Should().NotBeNull();

        var token = await verifyDb.RefreshTokens
            .FirstOrDefaultAsync(t => t.HubUserId == user!.Id);
        token.Should().NotBeNull();
    }

    [Fact]
    public async Task Register_DuplicateUsername_ReturnsUsernameTaken()
    {
        // Arrange - seed an existing user
        await using var seedDb = CreateDbContext();
        var encryption = new AesEncryptionService(TestEncryptionKey);
        seedDb.HubUsers.Add(new HubUser
        {
            Id = UserIdBase + 1,
            Username = "dupuser_username",
            DisplayName = "Dup User",
            Email = encryption.Encrypt("dupuser_username@test.invalid"),
            EmailHash = encryption.ComputeHmac("dupuser_username@test.invalid"),
            PasswordHash = "hashed",
            IsAdmin = false,
            IsDisabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = DateTimeOffset.UtcNow
        });
        await seedDb.SaveChangesAsync();

        // Act - register with the same username but a different email
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var result = await service.RegisterAsync(
            "dupuser_username",
            "Another User",
            "another_dup_username@test.invalid",
            "SecurePass1!",
            null,
            null,
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("USERNAME_TAKEN");
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsEmailTaken()
    {
        // Arrange - seed an existing user
        await using var seedDb = CreateDbContext();
        var encryption = new AesEncryptionService(TestEncryptionKey);
        const string sharedEmail = "dupuser_email@test.invalid";
        seedDb.HubUsers.Add(new HubUser
        {
            Id = UserIdBase + 2,
            Username = "dupuser_email_seed",
            DisplayName = "Dup Email Seed",
            Email = encryption.Encrypt(sharedEmail),
            EmailHash = encryption.ComputeHmac(sharedEmail),
            PasswordHash = "hashed",
            IsAdmin = false,
            IsDisabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = DateTimeOffset.UtcNow
        });
        await seedDb.SaveChangesAsync();

        // Act - register with a different username but the same email
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var result = await service.RegisterAsync(
            "dupuser_email_new",
            "Another User",
            sharedEmail,
            "SecurePass1!",
            null,
            null,
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("EMAIL_TAKEN");
    }

    [Fact]
    public async Task Register_DoesNotCallSaveChanges()
    {
        // The service must only add entities to the change tracker, never persist them.
        // This test verifies that a fresh DbContext cannot see the new user.

        // Arrange
        await using var db = CreateDbContext();
        var service = CreateService(db);

        // Act - do NOT call SaveChanges
        var result = await service.RegisterAsync(
            "unsaved_user",
            "Unsaved User",
            "unsaved_user@test.invalid",
            "SecurePass1!",
            null,
            null,
            CancellationToken.None);

        // Assert - result itself should succeed (entities added to tracker)
        result.IsSuccess.Should().BeTrue();

        // Verify the user does NOT exist in the database via a fresh context
        await using var verifyDb = CreateDbContext();
        var user = await verifyDb.HubUsers
            .FirstOrDefaultAsync(u => u.Username == "unsaved_user");
        user.Should().BeNull("the service must not persist without an explicit SaveChanges call from the caller");
    }

    [Fact]
    public async Task Register_PasswordIsHashed()
    {
        // Arrange
        await using var db = CreateDbContext();
        var service = CreateService(db);
        const string plainPassword = "PlainText_Password_1!";

        // Act
        var result = await service.RegisterAsync(
            "hashed_pass_user",
            "Hash Test User",
            "hashed_pass_user@test.invalid",
            plainPassword,
            null,
            null,
            CancellationToken.None);

        await db.SaveChangesAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();

        await using var verifyDb = CreateDbContext();
        var user = await verifyDb.HubUsers
            .FirstOrDefaultAsync(u => u.Username == "hashed_pass_user");
        user.Should().NotBeNull();
        user!.PasswordHash.Should().NotBe(plainPassword, "password must not be stored in plaintext");
        BCrypt.Net.BCrypt.Verify(plainPassword, user.PasswordHash).Should().BeTrue("stored hash must verify against the original password");
    }
}
