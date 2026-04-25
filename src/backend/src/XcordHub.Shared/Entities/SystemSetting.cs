namespace XcordHub.Entities;

/// <summary>
/// Represents a system-level setting (e.g., RSA keys for JWT signing).
/// </summary>
public sealed class SystemSetting
{
    /// <summary>
    /// Setting key (e.g., "EncryptedRsaPrivateKey", "RsaPublicKey").
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Setting value (stored as text).
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Setting creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Setting last update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
