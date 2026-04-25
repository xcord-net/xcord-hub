namespace XcordHub.Infrastructure.Services;

public interface IJwtService
{
    /// <summary>
    /// Generates a JWT access token for the specified hub user.
    /// </summary>
    string GenerateAccessToken(long userId, bool isAdmin);

    /// <summary>
    /// Ensures the RSA key pair exists in the database.
    /// Generates and stores a new key pair on first boot if needed.
    /// Private key is encrypted at rest using IEncryptionService.
    /// </summary>
    Task EnsureRsaKeyPairAsync(CancellationToken cancellationToken = default);
}
