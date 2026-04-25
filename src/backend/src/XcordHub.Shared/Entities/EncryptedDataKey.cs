namespace XcordHub.Entities;

/// <summary>
/// A versioned, KEK-wrapped Data Encryption Key (DEK) for the hub.
/// One row per key version; exactly one row has IsActive = true at any time.
/// New ciphertext is encrypted with the active version's DEK; existing ciphertext
/// continues to decrypt with whichever version it was encrypted under (selected
/// by the leading version byte of the ciphertext).
/// </summary>
public sealed class EncryptedDataKey
{
    /// <summary>
    /// Monotonically increasing key version number. Stored as a single byte in
    /// the leading position of every AES-GCM ciphertext.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// The DEK material (32 bytes) wrapped under the KEK using
    /// <see cref="XcordHub.Infrastructure.Services.KeyWrappingService"/>.
    /// </summary>
    public byte[] WrappedKey { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// True for the single row currently used for new encryptions.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// When this key was created (i.e., when this rotation occurred).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
