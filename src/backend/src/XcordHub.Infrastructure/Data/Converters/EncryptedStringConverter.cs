using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Infrastructure.Data.Converters;

/// <summary>
/// EF Core value converter that transparently encrypts string values to bytea on write
/// and decrypts bytea to string on read, using the hub's AES-256-GCM encryption service.
/// </summary>
public sealed class EncryptedStringConverter : ValueConverter<string, byte[]>
{
    public EncryptedStringConverter(IEncryptionService encryptionService)
        : base(
            plaintext => encryptionService.Encrypt(plaintext),
            ciphertext => encryptionService.Decrypt(ciphertext))
    {
    }
}
