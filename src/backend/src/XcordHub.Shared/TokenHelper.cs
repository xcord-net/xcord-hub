using System.Security.Cryptography;
using System.Text;

namespace XcordHub;

public static class TokenHelper
{
    /// <summary>
    /// Generates a cryptographically random 32-byte token encoded as Base64.
    /// </summary>
    public static string GenerateToken()
    {
        var randomBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    /// <summary>
    /// Computes a SHA-256 hex hash of the given token string (UTF-8 encoded).
    /// </summary>
    public static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hashBytes);
    }
}
