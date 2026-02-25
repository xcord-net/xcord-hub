namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Provides the Key-Encryption-Key (KEK) used to wrap/unwrap the Data-Encryption-Key (DEK).
/// Returns null when no KEK is configured (legacy/standalone mode).
/// </summary>
public interface IKekProvider
{
    byte[]? GetKek();
}
