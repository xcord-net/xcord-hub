using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Singleton that holds the loaded RSA public key for JWT validation.
/// Populated during bootstrap from the SystemSettings table.
/// </summary>
public sealed class RsaKeySingleton
{
    private RsaSecurityKey? _publicKey;
    private RSA? _publicRsa;
    private string? _kid;

    public void LoadPublicKey(string base64Key)
    {
        var publicKeyBytes = Convert.FromBase64String(base64Key);
        var rsa = RSA.Create();
        rsa.ImportRSAPublicKey(publicKeyBytes, out _);
        _publicRsa = rsa;
        _publicKey = new RsaSecurityKey(rsa);

        // Stable kid: SHA-256 over the DER-encoded public key, base64url
        var hash = SHA256.HashData(publicKeyBytes);
        _kid = Base64UrlEncoder.Encode(hash);
        _publicKey.KeyId = _kid;
    }

    public RsaSecurityKey GetPublicKey()
    {
        if (_publicKey == null)
        {
            throw new InvalidOperationException("RSA public key has not been loaded");
        }

        return _publicKey;
    }

    public RSAParameters GetPublicParameters()
    {
        if (_publicRsa == null)
        {
            throw new InvalidOperationException("RSA public key has not been loaded");
        }

        return _publicRsa.ExportParameters(false);
    }

    public string GetKeyId()
    {
        if (_kid == null)
        {
            throw new InvalidOperationException("RSA public key has not been loaded");
        }

        return _kid;
    }
}
