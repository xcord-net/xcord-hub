namespace XcordHub.Infrastructure.Services;

public interface IEncryptionService
{
    byte[] Encrypt(string plaintext);
    string Decrypt(byte[] ciphertext);
    byte[] ComputeHmac(string value);

    /// <summary>
    /// Computes HMAC-SHA256 over the supplied bytes using an instance-stable
    /// signing key dedicated to opaque cursor pagination tokens. Domain-separated
    /// from the blind-index HMAC.
    /// </summary>
    byte[] ComputeCursorHmac(byte[] data);
}
