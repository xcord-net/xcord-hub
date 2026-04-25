using System.Buffers.Binary;
using System.Security.Cryptography;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Encodes and decodes opaque, HMAC-signed cursor tokens for paginated
/// endpoints. The wire format is base64url(payload || tag) where payload is
/// the long ID encoded as 8-byte big-endian and tag is the first 16 bytes of
/// HMAC-SHA256(payload) using an instance-stable cursor signing key.
///
/// This prevents leaking raw Snowflake IDs (which encode timestamp + worker)
/// to API consumers and prevents tampering, since any modified payload will
/// fail HMAC verification.
/// </summary>
public interface ICursorService
{
    /// <summary>
    /// Encodes the supplied long ID as an opaque base64url cursor.
    /// </summary>
    string Encode(long id);

    /// <summary>
    /// Decodes and verifies an opaque cursor. Returns Success(null) for
    /// null/empty input, Success(id) for a valid cursor, and a Validation
    /// failure for malformed or tampered input.
    /// </summary>
    Result<long?> Decode(string? cursor);
}

public sealed class CursorService : ICursorService
{
    private const int PayloadSize = 8;
    private const int TagSize = 16;
    private const int TotalSize = PayloadSize + TagSize;

    private readonly IEncryptionService _encryptionService;

    public CursorService(IEncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    public string Encode(long id)
    {
        var payload = new byte[PayloadSize];
        BinaryPrimitives.WriteInt64BigEndian(payload, id);

        var fullTag = _encryptionService.ComputeCursorHmac(payload);

        var token = new byte[TotalSize];
        Buffer.BlockCopy(payload, 0, token, 0, PayloadSize);
        Buffer.BlockCopy(fullTag, 0, token, PayloadSize, TagSize);

        return Base64UrlEncode(token);
    }

    public Result<long?> Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return Result<long?>.Success(null);
        }

        byte[] decoded;
        try
        {
            decoded = Base64UrlDecode(cursor);
        }
        catch (FormatException)
        {
            return Error.Validation("INVALID_CURSOR", "Cursor is invalid or tampered");
        }

        if (decoded.Length != TotalSize)
        {
            return Error.Validation("INVALID_CURSOR", "Cursor is invalid or tampered");
        }

        var payload = new byte[PayloadSize];
        var providedTag = new byte[TagSize];
        Buffer.BlockCopy(decoded, 0, payload, 0, PayloadSize);
        Buffer.BlockCopy(decoded, PayloadSize, providedTag, 0, TagSize);

        var fullExpected = _encryptionService.ComputeCursorHmac(payload);
        var expectedTag = new byte[TagSize];
        Buffer.BlockCopy(fullExpected, 0, expectedTag, 0, TagSize);

        if (!CryptographicOperations.FixedTimeEquals(providedTag, expectedTag))
        {
            return Error.Validation("INVALID_CURSOR", "Cursor is invalid or tampered");
        }

        var id = BinaryPrimitives.ReadInt64BigEndian(payload);
        return Result<long?>.Success(id);
    }

    private static string Base64UrlEncode(byte[] data)
    {
        var base64 = Convert.ToBase64String(data);
        return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
            case 0: break;
            default: throw new FormatException("Invalid base64url length");
        }
        return Convert.FromBase64String(padded);
    }
}
