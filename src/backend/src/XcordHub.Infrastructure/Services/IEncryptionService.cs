namespace XcordHub.Infrastructure.Services;

public interface IEncryptionService
{
    byte[] Encrypt(string plaintext);
    string Decrypt(byte[] ciphertext);
    byte[] ComputeHmac(string value);
}
