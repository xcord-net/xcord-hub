using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Encryption service using AES-256-GCM (authenticated encryption) with HKDF key derivation.
///
/// Ciphertext format: [1-byte key-version][12-byte nonce][16-byte tag][ciphertext]
/// The leading byte names which DEK version was used (1..255). Version 1 corresponds to
/// the bootstrap DEK; subsequent rotations introduce versions 2, 3, ... Older versions
/// remain decryptable as long as their wrapped DEK row is still present in
/// encrypted_data_keys.
///
/// Legacy AES-CBC ciphertext (pre-versioning) is also supported on read: it has no
/// version prefix and is detected by length plus first-byte heuristics.
///
/// HMAC blind-index keys and cursor-signing keys are derived from the lowest
/// registered version so they remain STABLE across rotations.
/// </summary>
public sealed class AesEncryptionService : IEncryptionService
{
    private const int NonceSize = 12; // AES-GCM standard nonce size
    private const int TagSize = 16;   // AES-GCM standard tag size

    // Stable, non-secret per-deployment HKDF salt for the extract step.
    // Provides cryptographic domain separation so a leaked IKM in another context
    // cannot produce identical OKMs here. Must remain stable: changing it invalidates
    // all derived keys.
    private static readonly byte[] HkdfSalt =
        SHA256.HashData(Encoding.UTF8.GetBytes("xcord-hub-hkdf-salt-v1"));

    private readonly EncryptionKeyHolder _keyHolder;
    private readonly ConcurrentDictionary<byte, byte[]> _aesKeyCache = new();

    private readonly byte[] _hmacKey;
    private readonly byte[] _cursorKey;
    private readonly byte[] _legacyCbcKey;

    /// <summary>
    /// Production constructor: takes the singleton key holder so rotation is
    /// observable without rebuilding the service.
    /// </summary>
    public AesEncryptionService(EncryptionKeyHolder keyHolder)
    {
        _keyHolder = keyHolder ?? throw new ArgumentNullException(nameof(keyHolder));
        if (!keyHolder.IsInitialized)
        {
            throw new InvalidOperationException(
                "EncryptionKeyHolder has no keys registered. " +
                "Bootstrap must populate it before the encryption service is resolved.");
        }

        var stableMaterial = keyHolder.GetKey(keyHolder.Versions[0]);
        var stableMaterialBytes = Encoding.UTF8.GetBytes(stableMaterial);

        _hmacKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: stableMaterialBytes,
            outputLength: 32,
            salt: HkdfSalt,
            info: "xcord:hub:hmac-blind-index:v1"u8.ToArray());

        _cursorKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: stableMaterialBytes,
            outputLength: 32,
            salt: HkdfSalt,
            info: "xcord:hub:cursor-signing:v1"u8.ToArray());

        // CBC key for decrypting v0 ciphertext (AES-CBC format)
        using var sha256 = SHA256.Create();
        _legacyCbcKey = sha256.ComputeHash(Encoding.UTF8.GetBytes("aes:" + stableMaterial));
    }

    /// <summary>
    /// Single-key convenience constructor for tests and the design-time factory.
    /// </summary>
    public AesEncryptionService(string encryptionKey)
        : this(BuildSingleKeyHolder(encryptionKey))
    {
    }

    private static EncryptionKeyHolder BuildSingleKeyHolder(string encryptionKey)
    {
        if (string.IsNullOrWhiteSpace(encryptionKey))
            throw new ArgumentException("Encryption key cannot be null or empty.", nameof(encryptionKey));

        var holder = new EncryptionKeyHolder();
        holder.SetKey(encryptionKey);
        return holder;
    }

    /// <summary>
    /// Encrypts plaintext using the active DEK version under AES-256-GCM.
    /// </summary>
    public byte[] Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return Array.Empty<byte>();
        }

        var version = _keyHolder.ActiveVersion;
        var aesKey = GetAesKeyForVersion(version);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aesGcm = new AesGcm(aesKey, TagSize);
        aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var result = new byte[1 + NonceSize + TagSize + ciphertext.Length];
        result[0] = version;
        Buffer.BlockCopy(nonce, 0, result, 1, NonceSize);
        Buffer.BlockCopy(tag, 0, result, 1 + NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, 1 + NonceSize + TagSize, ciphertext.Length);

        return result;
    }

    /// <summary>
    /// Decrypts ciphertext. Selects the correct DEK by reading the leading version
    /// byte. Falls back to the legacy AES-CBC reader for un-versioned inputs.
    /// </summary>
    public string Decrypt(byte[] ciphertext)
    {
        if (ciphertext == null || ciphertext.Length == 0)
        {
            return string.Empty;
        }

        var versionByte = ciphertext[0];

        if (versionByte != 0
            && ciphertext.Length > 1 + NonceSize + TagSize
            && _keyHolder.TryGetKey(versionByte, out _))
        {
            return DecryptGcm(ciphertext, versionByte);
        }

        if (ciphertext.Length >= 32)
        {
            return DecryptLegacyCbc(ciphertext);
        }

        if (versionByte != 0 && ciphertext.Length > 1 + NonceSize + TagSize)
        {
            throw new CryptographicException($"Unknown key version {versionByte}");
        }

        throw new CryptographicException("Invalid ciphertext format");
    }

    private byte[] GetAesKeyForVersion(byte version)
    {
        return _aesKeyCache.GetOrAdd(version, v =>
        {
            var material = _keyHolder.GetKey(v);
            var materialBytes = Encoding.UTF8.GetBytes(material);
            return HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                ikm: materialBytes,
                outputLength: 32,
                salt: HkdfSalt,
                info: "xcord:hub:aes-gcm-encryption:v1"u8.ToArray());
        });
    }

    private string DecryptGcm(byte[] data, byte version)
    {
        var aesKey = GetAesKeyForVersion(version);

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertextLength = data.Length - 1 - NonceSize - TagSize;
        var ciphertext = new byte[ciphertextLength];
        var plaintext = new byte[ciphertextLength];

        Buffer.BlockCopy(data, 1, nonce, 0, NonceSize);
        Buffer.BlockCopy(data, 1 + NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(data, 1 + NonceSize + TagSize, ciphertext, 0, ciphertextLength);

        using var aesGcm = new AesGcm(aesKey, TagSize);
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
    /// Computes HMAC-SHA256 blind index for a value. Stable across rotations.
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

    /// <summary>
    /// Computes HMAC-SHA256 over the supplied bytes using the instance-stable
    /// cursor signing key. Domain-separated from the blind-index HMAC and stable
    /// across DEK rotations.
    /// </summary>
    public byte[] ComputeCursorHmac(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var hmac = new HMACSHA256(_cursorKey);
        return hmac.ComputeHash(data);
    }
}
