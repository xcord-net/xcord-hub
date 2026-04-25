using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Tests.Helpers;

/// <summary>
/// Creates JwtService instances for tests that need to issue tokens directly without
/// running the full WebApplicationFactory. Generates and persists an RSA key pair
/// (encrypted with the supplied DEK) the same way BootstrapService does at runtime.
/// </summary>
public static class JwtTestHelper
{
    public const string TestIssuer = "test-issuer";
    public const string TestAudience = "test-audience";

    public static IJwtService CreateJwtService(
        HubDbContext db,
        string encryptionKey,
        string issuer = TestIssuer,
        string audience = TestAudience,
        int expirationMinutes = 60)
    {
        var encryption = new AesEncryptionService(encryptionKey);
        var rsaKeySingleton = new RsaKeySingleton();
        var options = Options.Create(new JwtOptions
        {
            Issuer = issuer,
            Audience = audience,
            AccessTokenExpirationMinutes = expirationMinutes
        });

        var jwt = new JwtService(
            db,
            options,
            rsaKeySingleton,
            encryption,
            NullLogger<JwtService>.Instance);

        // Ensure the key pair exists, then load the public key into the singleton
        // so that any caller that subsequently validates a token has the key available.
        jwt.EnsureRsaKeyPairAsync().GetAwaiter().GetResult();
        var publicKey = db.SystemSettings.First(s => s.Key == JwtService.RsaPublicKeySettingKey);
        rsaKeySingleton.LoadPublicKey(publicKey.Value);

        return jwt;
    }
}
