namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Rotates the active hub Data Encryption Key (DEK). Generates a fresh 32-byte DEK,
/// wraps it under the KEK, persists it to encrypted_data_keys as a new version,
/// marks it active, and updates the in-memory key holder so subsequent calls to
/// <see cref="IEncryptionService.Encrypt"/> use the new version.
///
/// Existing ciphertext is NOT re-encrypted; it remains decryptable through the
/// older version's row in encrypted_data_keys.
/// </summary>
public interface IKeyRotationService
{
    /// <summary>
    /// Performs a rotation. Returns the new active version number.
    /// </summary>
    Task<int> RotateDataKeyAsync(CancellationToken cancellationToken = default);
}
