using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using XcordHub.Entities;
using XcordHub.Features.Auth;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Tests.Infrastructure;

/// <summary>
/// Integration tests for ForgotPasswordHandler.
/// Verifies the password reset flow: token creation in DB + email dispatch + reset completes.
/// Uses a real PostgreSQL instance via Testcontainers.
/// </summary>
[Trait("Category", "Auth")]
public sealed class ForgotPasswordHandlerTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private string _connectionString = string.Empty;
    private const string TestEncryptionKey = "test-encryption-key-with-256-bits-minimum-length-required";

    // ─── IAsyncLifetime ──────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("xcordhub_forgotpw_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        // Apply schema
        await using var ctx = CreateDbContext();
        await ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private HubDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
    }

    private static AesEncryptionService CreateEncryptionService() =>
        new(TestEncryptionKey);

    /// <summary>
    /// Creates a real HubUser in the DB and returns its plaintext email.
    /// </summary>
    private async Task<(HubUser user, string email)> SeedUserAsync(
        HubDbContext db,
        AesEncryptionService enc,
        string suffix = "")
    {
        var email = $"reset{suffix}_{Guid.NewGuid():N}@test.local";
        var emailHash = enc.ComputeHmac(email.ToLowerInvariant());
        var encryptedEmail = enc.Encrypt(email.ToLowerInvariant());
        var snowflake = new SnowflakeId(5);

        var user = new HubUser
        {
            Id = snowflake.NextId(),
            Username = $"resetuser{suffix}_{Guid.NewGuid():N}"[..30],
            DisplayName = $"Reset User {suffix}",
            Email = encryptedEmail,
            EmailHash = emailHash,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPass123!", 4), // low work factor for tests
            IsAdmin = false,
            IsDisabled = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.HubUsers.Add(user);
        await db.SaveChangesAsync();
        return (user, email);
    }

    private static ForgotPasswordHandler CreateForgotPasswordHandler(
        HubDbContext db,
        AesEncryptionService enc,
        IEmailService emailService,
        string hubBaseUrl = "https://xcord-dev.net")
    {
        var emailOptions = Options.Create(new EmailOptions
        {
            SmtpHost = "localhost",
            SmtpPort = 1025,
            FromAddress = "noreply@xcord.local",
            FromName = "Xcord",
            UseSsl = false,
            DevMode = true,
            HubBaseUrl = hubBaseUrl
        });

        return new ForgotPasswordHandler(
            db,
            enc,
            emailService,
            emailOptions,
            new SnowflakeId(6),
            NullLogger<ForgotPasswordHandler>.Instance);
    }

    private static ResetPasswordHandler CreateResetPasswordHandler(HubDbContext db) =>
        new(db);

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ForgotPassword_WithRegisteredEmail_CreatesResetTokenInDatabase()
    {
        // Arrange
        await using var db = CreateDbContext();
        var enc = CreateEncryptionService();
        var emailSink = new CapturedEmailService();

        var (user, email) = await SeedUserAsync(db, enc);
        var handler = CreateForgotPasswordHandler(db, enc, emailSink);

        // Act
        var result = await handler.Handle(
            new ForgotPasswordCommand(email),
            CancellationToken.None);

        // Assert — handler returns success (bool)
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();

        // Verify a PasswordResetToken was persisted
        await using var verifyDb = CreateDbContext();
        var token = await verifyDb.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.HubUserId == user.Id);

        token.Should().NotBeNull("a reset token must be stored in the database");
        token!.IsUsed.Should().BeFalse("token must not be consumed yet");
        token.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow, "token must be valid for at least some time");
        token.TokenHash.Should().NotBeNullOrEmpty("the hash must be persisted");
    }

    [Fact]
    public async Task ForgotPassword_WithRegisteredEmail_SendsResetEmail()
    {
        // Arrange
        await using var db = CreateDbContext();
        var enc = CreateEncryptionService();
        var emailSink = new CapturedEmailService();

        var (_, email) = await SeedUserAsync(db, enc, "email");
        var handler = CreateForgotPasswordHandler(db, enc, emailSink);

        // Act
        await handler.Handle(new ForgotPasswordCommand(email), CancellationToken.None);

        // Assert
        emailSink.SentEmails.Should().HaveCount(1, "exactly one email should be sent");
        var sent = emailSink.SentEmails[0];
        sent.To.Should().Be(email.ToLowerInvariant());
        sent.Subject.Should().Contain("password");
        sent.HtmlBody.Should().Contain("reset-password?token=", "body must contain the reset URL");
        sent.HtmlBody.Should().Contain("xcord-dev.net", "base URL must appear in the link");
    }

    [Fact]
    public async Task ForgotPassword_WithUnknownEmail_ReturnsSuccessWithoutCreatingToken()
    {
        // Arrange
        await using var db = CreateDbContext();
        var enc = CreateEncryptionService();
        var emailSink = new CapturedEmailService();
        var handler = CreateForgotPasswordHandler(db, enc, emailSink);

        var countBefore = await db.PasswordResetTokens.CountAsync();

        // Act — email enumeration protection: always returns 204/success
        var result = await handler.Handle(
            new ForgotPasswordCommand("nobody@notexist.example"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue("handler must not leak whether the email exists");
        emailSink.SentEmails.Should().BeEmpty("no email should be dispatched for an unknown address");
        var countAfter = await db.PasswordResetTokens.CountAsync();
        countAfter.Should().Be(countBefore, "no token must be written for an unknown address");
    }

    [Fact]
    public async Task ForgotPassword_WithDisabledUser_ReturnsSuccessWithoutCreatingToken()
    {
        // Arrange
        await using var db = CreateDbContext();
        var enc = CreateEncryptionService();
        var emailSink = new CapturedEmailService();

        var (user, email) = await SeedUserAsync(db, enc, "disabled");
        // Disable the user
        user.IsDisabled = true;
        await db.SaveChangesAsync();

        var handler = CreateForgotPasswordHandler(db, enc, emailSink);
        var countBefore = await db.PasswordResetTokens.CountAsync();

        // Act
        var result = await handler.Handle(new ForgotPasswordCommand(email), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue("disabled users must not reveal account existence");
        emailSink.SentEmails.Should().BeEmpty();
        var countAfter = await db.PasswordResetTokens.CountAsync();
        countAfter.Should().Be(countBefore);
    }

    [Fact]
    public async Task ForgotPassword_FullResetFlow_AllowsLoginWithNewPassword()
    {
        // Arrange — create user
        await using var db = CreateDbContext();
        var enc = CreateEncryptionService();
        var emailSink = new CapturedEmailService();

        var (_, email) = await SeedUserAsync(db, enc, "fullflow");
        var forgotHandler = CreateForgotPasswordHandler(db, enc, emailSink);

        // Step 1: request password reset
        await forgotHandler.Handle(new ForgotPasswordCommand(email), CancellationToken.None);

        // Extract reset token from the email body URL
        var emailBody = emailSink.SentEmails[0].HtmlBody;
        var tokenStart = emailBody.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
        var tokenEnd = emailBody.IndexOf('"', tokenStart);
        var rawToken = Uri.UnescapeDataString(emailBody[tokenStart..tokenEnd]);

        rawToken.Should().NotBeNullOrEmpty("the reset token must be in the email body");

        // Step 2: use token to reset password
        await using var resetDb = CreateDbContext();
        var resetHandler = CreateResetPasswordHandler(resetDb);
        const string newPassword = "NewPass456!";

        var resetResult = await resetHandler.Handle(
            new ResetPasswordCommand(rawToken, newPassword),
            CancellationToken.None);

        resetResult.IsSuccess.Should().BeTrue("the token should be valid and the password should update");

        // Step 3: verify the token is now marked as used
        await using var verifyDb = CreateDbContext();
        var usedToken = await verifyDb.PasswordResetTokens.FirstOrDefaultAsync(t => t.IsUsed);
        usedToken.Should().NotBeNull("the used token must be marked IsUsed=true");

        // Step 4: verify the updated password hash matches the new password
        var updatedUser = await verifyDb.HubUsers
            .FirstOrDefaultAsync(u => u.EmailHash == enc.ComputeHmac(email.ToLowerInvariant()));
        updatedUser.Should().NotBeNull();
        BCrypt.Net.BCrypt.Verify(newPassword, updatedUser!.PasswordHash)
            .Should().BeTrue("the stored password hash must verify against the new password");
    }

    [Fact]
    public async Task ResetPassword_WithUsedToken_ReturnsError()
    {
        // Arrange
        await using var db = CreateDbContext();
        var enc = CreateEncryptionService();
        var emailSink = new CapturedEmailService();

        var (_, email) = await SeedUserAsync(db, enc, "usedtoken");
        var forgotHandler = CreateForgotPasswordHandler(db, enc, emailSink);
        await forgotHandler.Handle(new ForgotPasswordCommand(email), CancellationToken.None);

        var emailBody = emailSink.SentEmails[0].HtmlBody;
        var tokenStart = emailBody.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
        var tokenEnd = emailBody.IndexOf('"', tokenStart);
        var rawToken = Uri.UnescapeDataString(emailBody[tokenStart..tokenEnd]);

        // Use the token once
        await using var resetDb1 = CreateDbContext();
        var resetHandler1 = CreateResetPasswordHandler(resetDb1);
        await resetHandler1.Handle(new ResetPasswordCommand(rawToken, "NewPass456!"), CancellationToken.None);

        // Act — try to use the same token again
        await using var resetDb2 = CreateDbContext();
        var resetHandler2 = CreateResetPasswordHandler(resetDb2);
        var result = await resetHandler2.Handle(new ResetPasswordCommand(rawToken, "AnotherPass789!"), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue("an already-used token must be rejected");
        result.Error!.Code.Should().Be("TOKEN_USED");
    }

    [Fact]
    public async Task ResetPassword_WithInvalidToken_ReturnsError()
    {
        // Arrange
        await using var db = CreateDbContext();
        var resetHandler = CreateResetPasswordHandler(db);

        // Act
        var result = await resetHandler.Handle(
            new ResetPasswordCommand("not-a-valid-token", "NewPass456!"),
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue("an invalid token must be rejected");
        result.Error!.Code.Should().Be("INVALID_TOKEN");
    }
}

/// <summary>
/// In-memory email sink for integration tests — captures all sent emails without SMTP.
/// </summary>
file sealed class CapturedEmailService : IEmailService
{
    private readonly List<SentEmail> _emails = [];
    public IReadOnlyList<SentEmail> SentEmails => _emails;

    public Task SendAsync(string to, string subject, string htmlBody)
    {
        _emails.Add(new SentEmail(to, subject, htmlBody));
        return Task.CompletedTask;
    }
}

file sealed record SentEmail(string To, string Subject, string HtmlBody);
