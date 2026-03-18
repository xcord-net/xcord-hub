using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Auth;

public sealed record RegistrationResult(HubUser User, string AccessToken, string RefreshToken);

public sealed class UserRegistrationService(
    HubDbContext db,
    ICaptchaService captchaService,
    IEncryptionService encryptionService,
    IJwtService jwtService,
    SnowflakeIdGenerator idGenerator,
    IOptions<AuthOptions> authOptions)
{
    private readonly AuthOptions _authOptions = authOptions.Value;

    public async Task<Result<RegistrationResult>> RegisterAsync(
        string username,
        string displayName,
        string email,
        string password,
        string? captchaId,
        string? captchaAnswer,
        CancellationToken ct)
    {
        // Validate captcha
        if (!await captchaService.ValidateAsync(captchaId ?? "", captchaAnswer ?? ""))
        {
            return Error.BadRequest("CAPTCHA_FAILED", "Invalid or expired captcha");
        }

        // Check if username already exists
        var usernameExists = await db.HubUsers
            .AnyAsync(u => u.Username == username, ct);

        if (usernameExists)
        {
            return Error.Conflict("USERNAME_TAKEN", "Username is already taken");
        }

        // Check if email already exists (by EmailHash)
        var emailHash = encryptionService.ComputeHmac(email.ToLowerInvariant());
        var emailExists = await db.HubUsers
            .AnyAsync(u => u.EmailHash == emailHash, ct);

        if (emailExists)
        {
            return Error.Conflict("EMAIL_TAKEN", "Email is already registered");
        }

        // Hash password (BCrypt, configurable work factor) - offloaded to thread pool to avoid starvation
        var passwordHash = await Task.Run(() => BCrypt.Net.BCrypt.HashPassword(password, _authOptions.BcryptWorkFactor));

        // Encrypt email
        var encryptedEmail = encryptionService.Encrypt(email.ToLowerInvariant());

        // Create user
        var userId = idGenerator.NextId();
        var now = DateTimeOffset.UtcNow;

        var user = new HubUser
        {
            Id = userId,
            Username = username,
            DisplayName = displayName,
            Email = encryptedEmail,
            EmailHash = emailHash,
            PasswordHash = passwordHash,
            IsAdmin = false,
            IsDisabled = false,
            CreatedAt = now,
            LastLoginAt = now
        };

        db.HubUsers.Add(user);

        // Create refresh token (30 days)
        var refreshTokenValue = TokenHelper.GenerateToken();
        var refreshTokenHash = TokenHelper.HashToken(refreshTokenValue);
        var refreshToken = new RefreshToken
        {
            Id = idGenerator.NextId(),
            TokenHash = refreshTokenHash,
            HubUserId = userId,
            ExpiresAt = now.AddDays(30),
            CreatedAt = now
        };

        db.RefreshTokens.Add(refreshToken);

        // Generate JWT access token
        var accessToken = jwtService.GenerateAccessToken(userId, user.IsAdmin);

        return new RegistrationResult(user, accessToken, refreshTokenValue);
    }
}
