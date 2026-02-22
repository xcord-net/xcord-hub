using System.Security.Cryptography;
using System.Text;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Encryption service using AES-256-GCM (authenticated encryption) with HKDF key derivation.
/// Ciphertext format: [1-byte version][12-byte nonce][16-byte tag][ciphertext]
/// Version 0x01 = AES-GCM (current), Version 0x00 = AES-CBC (read-only; re-encrypted to v1 on next write).
/// </summary>
public sealed class AesEncryptionService : IEncryptionService
{
    private const byte VersionGcm = 0x01;
    private const byte VersionLegacyCbc = 0x00;
    private const int NonceSize = 12; // AES-GCM standard nonce size
    private const int TagSize = 16;   // AES-GCM standard tag size

    private readonly byte[] _encryptionKey;
    private readonly byte[] _hmacKey;
    private readonly byte[] _legacyCbcKey; // For decrypting v0 (AES-CBC) ciphertext

    public AesEncryptionService(string encryptionKey)
    {
        if (string.IsNullOrWhiteSpace(encryptionKey))
        {
            throw new ArgumentException("Encryption key cannot be null or empty.", nameof(encryptionKey));
        }

        var keyBytes = Encoding.UTF8.GetBytes(encryptionKey);

        // Derive keys using HKDF
        _encryptionKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            keyBytes,
            outputLength: 32,
            info: "xcord-hub-aes-gcm-encryption"u8.ToArray());

        _hmacKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            keyBytes,
            outputLength: 32,
            info: "xcord-hub-hmac-blind-index"u8.ToArray());

        // CBC key for decrypting v0 ciphertext (AES-CBC format)
        using var sha256 = SHA256.Create();
        _legacyCbcKey = sha256.ComputeHash(Encoding.UTF8.GetBytes("aes:" + encryptionKey));
    }

    /// <summary>
    /// Encrypts plaintext using AES-256-GCM (authenticated encryption).
    /// </summary>
    public byte[] Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return Array.Empty<byte>();
        }

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aesGcm = new AesGcm(_encryptionKey, TagSize);
        aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Format: [version(1)][nonce(12)][tag(16)][ciphertext(N)]
        var result = new byte[1 + NonceSize + TagSize + ciphertext.Length];
        result[0] = VersionGcm;
        Buffer.BlockCopy(nonce, 0, result, 1, NonceSize);
        Buffer.BlockCopy(tag, 0, result, 1 + NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, 1 + NonceSize + TagSize, ciphertext.Length);

        return result;
    }

    /// <summary>
    /// Decrypts ciphertext. Supports both AES-GCM (v1) and AES-CBC (v0) formats.
    /// </summary>
    public string Decrypt(byte[] ciphertext)
    {
        if (ciphertext == null || ciphertext.Length == 0)
        {
            return string.Empty;
        }

        // Check if this is versioned ciphertext (v1 = AES-GCM)
        if (ciphertext[0] == VersionGcm && ciphertext.Length > 1 + NonceSize + TagSize)
        {
            return DecryptGcm(ciphertext);
        }

        // AES-CBC (v0) format — IV is 16 bytes, so minimum ciphertext length is 32
        if (ciphertext.Length >= 32)
        {
            return DecryptLegacyCbc(ciphertext);
        }

        throw new CryptographicException("Invalid ciphertext format");
    }

    private string DecryptGcm(byte[] data)
    {
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertextLength = data.Length - 1 - NonceSize - TagSize;
        var ciphertext = new byte[ciphertextLength];
        var plaintext = new byte[ciphertextLength];

        Buffer.BlockCopy(data, 1, nonce, 0, NonceSize);
        Buffer.BlockCopy(data, 1 + NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(data, 1 + NonceSize + TagSize, ciphertext, 0, ciphertextLength);

        using var aesGcm = new AesGcm(_encryptionKey, TagSize);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    private string DecryptLegacyCbc(byte[] ciphertext)
    {
        using var aes = Aes.Create();
        aes.Key = _legacyCbcKey;

        var iv = new byte[16];
        Buffer.BlockCopy(ciphertext, 0, iv, 0, iv.Length);
        aes.IV = iv;

        var actualCiphertext = new byte[ciphertext.Length - iv.Length];
        Buffer.BlockCopy(ciphertext, iv.Length, actualCiphertext, 0, actualCiphertext.Length);

        using var decryptor = aes.CreateDecryptor();
        var plaintextBytes = decryptor.TransformFinalBlock(actualCiphertext, 0, actualCiphertext.Length);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    /// <summary>
    /// Computes HMAC-SHA256 blind index for a value.
    /// </summary>
    public byte[] ComputeHmac(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Array.Empty<byte>();
        }

        using var hmac = new HMACSHA256(_hmacKey);
        var valueBytes = Encoding.UTF8.GetBytes(value);
        return hmac.ComputeHash(valueBytes);
    }
}
