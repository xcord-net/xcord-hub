using System.Collections.Concurrent;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Holds the active and historical Data Encryption Keys (DEKs) for transparent
/// multi-key decryption with periodic rotation.
///
/// New ciphertext is encrypted under the active version. Existing ciphertext
/// continues to decrypt with whichever version is encoded in its leading byte.
/// HMAC blind-index and cursor signing keys are derived from the original
/// version (1) and remain stable across rotations so blind-index lookups and
/// in-flight signed cursors keep working.
///
/// Versions are stored as a single byte (1..255). Version 0 is reserved as a
/// sentinel for legacy AES-CBC ciphertext (which has no version prefix).
/// </summary>
public sealed class EncryptionKeyHolder
{
    private readonly ConcurrentDictionary<byte, string> _keysByVersion = new();
    private byte _activeVersion;

    public byte ActiveVersion =>
        _activeVersion != 0 ? _activeVersion
            : throw new InvalidOperationException("No active encryption key version registered");

    public IReadOnlyList<byte> Versions
    {
        get
        {
            var v = _keysByVersion.Keys.ToList();
            v.Sort();
            return v;
        }
    }

    public bool IsInitialized => _activeVersion != 0;

    public string Key => GetKey(ActiveVersion);

    /// <summary>
    /// Single-key initialization. Registers the supplied material as version 1
    /// and sets it as the active version.
    /// </summary>
    public void SetKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Encryption key cannot be null or empty.", nameof(key));

        AddKey(version: 1, keyMaterial: key, isActive: true);
    }

    public void AddKey(byte version, string keyMaterial, bool isActive)
    {
        if (version == 0)
            throw new ArgumentOutOfRangeException(nameof(version),
                "Version 0 is reserved for legacy ciphertext.");
        if (string.IsNullOrWhiteSpace(keyMaterial))
            throw new ArgumentException("Key material cannot be null or empty.", nameof(keyMaterial));

        _keysByVersion[version] = keyMaterial;
        if (isActive)
            _activeVersion = version;
    }

    public void SetActiveVersion(byte version)
    {
        if (!_keysByVersion.ContainsKey(version))
            throw new InvalidOperationException(
                $"Cannot activate unknown key version {version}.");
        _activeVersion = version;
    }

    public string GetKey(byte version)
    {
        if (!_keysByVersion.TryGetValue(version, out var key))
            throw new System.Security.Cryptography.CryptographicException(
                $"Unknown key version {version}");
        return key;
    }

    public bool TryGetKey(byte version, out string keyMaterial)
    {
        if (_keysByVersion.TryGetValue(version, out var key))
        {
            keyMaterial = key;
            return true;
        }
        keyMaterial = string.Empty;
        return false;
    }
}
