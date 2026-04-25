using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Service for generating JWT access tokens using RS256 asymmetric signing.
/// RSA private key is encrypted at rest using the hub DEK via IEncryptionService.
/// </summary>
public sealed class JwtService : IJwtService
{
    public const string EncryptedRsaPrivateKeySettingKey = "EncryptedRsaPrivateKey";
    public const string RsaPublicKeySettingKey = "RsaPublicKey";

    private readonly HubDbContext _dbContext;
    private readonly JwtOptions _jwtOptions;
    private readonly RsaKeySingleton _rsaKeySingleton;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<JwtService> _logger;
    private RSA? _rsa;

    public JwtService(
        HubDbContext dbContext,
        IOptions<JwtOptions> jwtOptions,
        RsaKeySingleton rsaKeySingleton,
        IEncryptionService encryptionService,
        ILogger<JwtService> logger)
    {
        _dbContext = dbContext;
        _jwtOptions = jwtOptions.Value;
        _rsaKeySingleton = rsaKeySingleton;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    /// <summary>
    /// Ensures the RSA key pair exists in the database.
    /// Private key is always stored encrypted with the hub DEK.
    /// </summary>
    public async Task EnsureRsaKeyPairAsync(CancellationToken cancellationToken = default)
    {
        var encryptedKeySetting = await _dbContext.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == EncryptedRsaPrivateKeySettingKey, cancellationToken);

        if (encryptedKeySetting != null)
        {
            // Already present - nothing to do
            return;
        }

        // First boot: generate new RSA key pair, encrypt private key at rest
        using var rsa = RSA.Create(3072);
        var privateKey = Convert.ToBase64String(rsa.ExportRSAPrivateKey());
        var publicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());

        var encryptedBytes = _encryptionService.Encrypt(privateKey);
        var now = DateTimeOffset.UtcNow;

        _dbContext.SystemSettings.Add(new SystemSetting
        {
            Key = EncryptedRsaPrivateKeySettingKey,
            Value = Convert.ToBase64String(encryptedBytes),
            CreatedAt = now,
            UpdatedAt = now
        });

        _dbContext.SystemSettings.Add(new SystemSetting
        {
            Key = RsaPublicKeySettingKey,
            Value = publicKey,
            CreatedAt = now,
            UpdatedAt = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Generated new RSA key pair for hub JWT signing (RS256, 3072-bit)");
    }

    /// <summary>
    /// Generates a JWT access token for the specified user using RS256.
    /// </summary>
    public string GenerateAccessToken(long userId, bool isAdmin)
    {
        var rsa = GetRsa();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("admin", isAdmin.ToString().ToLower())
        };

        var signingKey = new RsaSecurityKey(rsa)
        {
            KeyId = _rsaKeySingleton.GetKeyId()
        };

        var credentials = new SigningCredentials(
            signingKey,
            SecurityAlgorithms.RsaSha256);

        var expires = DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = _jwtOptions.AccessTokenExpirationMinutes < 0 ? expires.AddMinutes(-1) : DateTime.UtcNow,
            IssuedAt = _jwtOptions.AccessTokenExpirationMinutes < 0 ? expires.AddMinutes(-1) : DateTime.UtcNow,
            Expires = expires,
            Issuer = _jwtOptions.Issuer,
            Audience = _jwtOptions.Audience,
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// Loads the RSA private key from the database, decrypting it from encrypted storage.
    /// </summary>
    private RSA GetRsa()
    {
        if (_rsa != null)
        {
            return _rsa;
        }

        var encryptedKeySetting = _dbContext.SystemSettings
            .FirstOrDefault(s => s.Key == EncryptedRsaPrivateKeySettingKey);

        if (encryptedKeySetting == null)
        {
            throw new InvalidOperationException(
                "RSA key pair not found in database. Call EnsureRsaKeyPairAsync first.");
        }

        var encryptedBytes = Convert.FromBase64String(encryptedKeySetting.Value);
        var privateKeyBase64 = _encryptionService.Decrypt(encryptedBytes);
        var privateKeyBytes = Convert.FromBase64String(privateKeyBase64);
        _rsa = RSA.Create();
        _rsa.ImportRSAPrivateKey(privateKeyBytes, out _);
        return _rsa;
    }
}
