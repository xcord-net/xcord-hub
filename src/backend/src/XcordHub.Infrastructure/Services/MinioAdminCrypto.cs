using System.Security.Cryptography;
using Konscious.Security.Cryptography;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Implements MinIO Admin API body encryption, compatible with the Go madmin-go EncryptData format.
/// Format: salt(32) | AEAD_ID(1) | nonce(8) | sio_encrypted_data
///
/// The sio format (secure-io/sio-go) uses AES-256-GCM with DARE (Data At Rest Encryption):
/// - Key derived via Argon2id(password, salt, time=1, memory=64MiB, threads=4, keyLen=32)
/// - Header seal: GCM(key, nonce[seqNum=0], nil plaintext, nil AAD) → 16-byte tag (internal, not written to output)
/// - Data seal: GCM(key, nonce[seqNum=1], plaintext, AAD=[0x80|header_tag]) → ciphertext|tag written to output
/// </summary>
public static class MinioAdminCrypto
{
    private const byte AeadIdArgon2IdAesGcm = 0x00;
    private const int Argon2IdTime = 1;
    private const int Argon2IdMemory = 64 * 1024; // 64 MiB (unit is KiB)
    private const int Argon2IdThreads = 4;
    private const int KeyLength = 32;
    private const int SaltLength = 32;
    private const int UserNonceLength = 8;
    private const int GcmNonceLength = 12; // AES-GCM standard
    private const int GcmTagLength = 16;

    /// <summary>
    /// Encrypts data using the same format as Go madmin-go EncryptData.
    /// Compatible with MinIO Admin REST API endpoints that expect encrypted bodies.
    /// </summary>
    public static byte[] EncryptData(string password, byte[] data)
    {
        // 1. Generate random salt
        var salt = RandomNumberGenerator.GetBytes(SaltLength);

        // 2. Derive key using Argon2id
        var key = DeriveKey(password, salt);

        // 3. Generate random 8-byte nonce (sio-go NonceSize for AES-256-GCM = GCM_NonceSize - 4 = 8)
        var userNonce = RandomNumberGenerator.GetBytes(UserNonceLength);

        // 4. Header seal: seal empty plaintext with nil AAD, using seqNum=0
        //    This produces a 16-byte GCM tag stored as internal AAD for data chunks
        var headerNonce = BuildGcmNonce(userNonce, 0);
        var headerTag = new byte[GcmTagLength];
        using var headerGcm = new AesGcm(key, GcmTagLength);
        headerGcm.Encrypt(headerNonce, Array.Empty<byte>(), Array.Empty<byte>(), headerTag);

        // 6. Build AAD for data chunk: [flag=0x80 (final)] + [header_tag (16 bytes)]
        var aad = new byte[1 + GcmTagLength];
        aad[0] = 0x80; // final flag
        Buffer.BlockCopy(headerTag, 0, aad, 1, GcmTagLength);

        // 7. Data seal: encrypt plaintext with seqNum=1, AAD = [flag|header_tag]
        var dataNonce = BuildGcmNonce(userNonce, 1);
        var ciphertext = new byte[data.Length];
        var dataTag = new byte[GcmTagLength];
        using var dataGcm = new AesGcm(key, GcmTagLength);
        dataGcm.Encrypt(dataNonce, data, ciphertext, dataTag, aad);

        // 8. Assemble output: salt(32) | AEAD_ID(1) | user_nonce(8) | ciphertext | tag(16)
        //    The flag byte (0x80) is in the AAD only, NOT in the output stream.
        var output = new byte[SaltLength + 1 + UserNonceLength + data.Length + GcmTagLength];
        var offset = 0;

        Buffer.BlockCopy(salt, 0, output, offset, SaltLength);
        offset += SaltLength;

        output[offset++] = AeadIdArgon2IdAesGcm;

        Buffer.BlockCopy(userNonce, 0, output, offset, UserNonceLength);
        offset += UserNonceLength;

        Buffer.BlockCopy(ciphertext, 0, output, offset, ciphertext.Length);
        offset += ciphertext.Length;

        Buffer.BlockCopy(dataTag, 0, output, offset, GcmTagLength);

        return output;
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var argon2 = new Argon2id(System.Text.Encoding.UTF8.GetBytes(password));
        argon2.Salt = salt;
        argon2.DegreeOfParallelism = Argon2IdThreads;
        argon2.MemorySize = Argon2IdMemory;
        argon2.Iterations = Argon2IdTime;
        return argon2.GetBytes(KeyLength);
    }

    private static byte[] BuildGcmNonce(byte[] userNonce, uint seqNum)
    {
        var nonce = new byte[GcmNonceLength];
        Buffer.BlockCopy(userNonce, 0, nonce, 0, UserNonceLength);
        BitConverter.TryWriteBytes(nonce.AsSpan(UserNonceLength), seqNum);
        return nonce;
    }
}
