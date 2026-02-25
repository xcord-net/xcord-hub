using System.Security.Cryptography;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Wraps/unwraps a DEK using a KEK via AES-256-GCM with HKDF key derivation.
/// Wrapped format: [0x02 version][12-byte nonce][16-byte tag][wrapped DEK]
/// Version 0x02 distinguishes wrapped keys from plaintext base64 strings.
/// </summary>
public static class KeyWrappingService
{
    private const byte VersionWrapped = 0x02;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] HkdfInfo = "xcord-hub-dek-wrapping"u8.ToArray();

    /// <summary>
    /// Wraps (encrypts) a DEK using the provided KEK.
    /// </summary>
    public static byte[] WrapDek(byte[] dek, byte[] kek)
    {
        var wrappingKey = DeriveWrappingKey(kek);

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[dek.Length];
        var tag = new byte[TagSize];

        using var aesGcm = new AesGcm(wrappingKey, TagSize);
        aesGcm.Encrypt(nonce, dek, ciphertext, tag);

        // Format: [version(1)][nonce(12)][tag(16)][ciphertext(N)]
        var result = new byte[1 + NonceSize + TagSize + ciphertext.Length];
        result[0] = VersionWrapped;
        Buffer.BlockCopy(nonce, 0, result, 1, NonceSize);
        Buffer.BlockCopy(tag, 0, result, 1 + NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, 1 + NonceSize + TagSize, ciphertext.Length);

        return result;
    }

    /// <summary>
    /// Unwraps (decrypts) a wrapped DEK using the provided KEK.
    /// </summary>
    public static byte[] UnwrapDek(byte[] wrapped, byte[] kek)
    {
        if (wrapped.Length < 1 + NonceSize + TagSize + 1)
            throw new CryptographicException("Wrapped key is too short");

        if (wrapped[0] != VersionWrapped)
            throw new CryptographicException($"Unexpected version byte: 0x{wrapped[0]:X2}");

        var wrappingKey = DeriveWrappingKey(kek);

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertextLength = wrapped.Length - 1 - NonceSize - TagSize;
        var ciphertext = new byte[ciphertextLength];
        var plaintext = new byte[ciphertextLength];

        Buffer.BlockCopy(wrapped, 1, nonce, 0, NonceSize);
        Buffer.BlockCopy(wrapped, 1 + NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(wrapped, 1 + NonceSize + TagSize, ciphertext, 0, ciphertextLength);

        using var aesGcm = new AesGcm(wrappingKey, TagSize);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    /// <summary>
    /// Checks if a stored value is a wrapped key (starts with version byte 0x02).
    /// </summary>
    public static bool IsWrapped(byte[] data)
    {
        return data.Length > 0 && data[0] == VersionWrapped;
    }

    /// <summary>
    /// Checks if a base64-encoded stored value is a wrapped key.
    /// </summary>
    public static bool IsWrappedBase64(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return false;
        try
        {
            var bytes = Convert.FromBase64String(base64);
            return IsWrapped(bytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] DeriveWrappingKey(byte[] kek)
    {
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            kek,
            outputLength: 32,
            info: HkdfInfo);
    }
}
