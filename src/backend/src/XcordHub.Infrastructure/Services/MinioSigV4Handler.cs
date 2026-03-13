using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// DelegatingHandler that signs HTTP requests using AWS Signature Version 4,
/// compatible with MinIO's Admin REST API (service="s3", region="").
/// </summary>
public sealed class MinioSigV4Handler : DelegatingHandler
{
    private readonly string _accessKey;
    private readonly string _secretKey;
    private const string Service = "s3";
    private const string Region = ""; // MinIO admin API uses empty region

    public MinioSigV4Handler(string accessKey, string secretKey)
    {
        _accessKey = accessKey;
        _secretKey = secretKey;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        // Read body for content hash
        byte[] bodyBytes = [];
        if (request.Content != null)
            bodyBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);

        var contentHash = HexHash(bodyBytes);

        // Set required headers
        request.Headers.TryAddWithoutValidation("X-Amz-Date", amzDate);
        request.Headers.TryAddWithoutValidation("X-Amz-Content-Sha256", contentHash);

        // Build canonical request
        var uri = request.RequestUri!;
        var canonicalPath = uri.AbsolutePath;
        var canonicalQuery = uri.Query.TrimStart('?');
        if (!string.IsNullOrEmpty(canonicalQuery))
            canonicalQuery = string.Join("&", canonicalQuery.Split('&').OrderBy(s => s));

        var signedHeaders = BuildSignedHeaders(request);
        var canonicalHeaders = BuildCanonicalHeaders(request, signedHeaders);

        var canonicalRequest = string.Join("\n",
            request.Method.Method,
            canonicalPath,
            canonicalQuery,
            canonicalHeaders,
            string.Join(";", signedHeaders),
            contentHash);

        // Build string to sign
        var credentialScope = $"{dateStamp}/{Region}/{Service}/aws4_request";
        var stringToSign = string.Join("\n",
            "AWS4-HMAC-SHA256",
            amzDate,
            credentialScope,
            HexHash(Encoding.UTF8.GetBytes(canonicalRequest)));

        // Calculate signature
        var signingKey = GetSigningKey(dateStamp);
        var signature = HexEncode(HmacSha256(signingKey, stringToSign));

        // Set Authorization header
        var authHeader = $"AWS4-HMAC-SHA256 Credential={_accessKey}/{credentialScope}, " +
                         $"SignedHeaders={string.Join(";", signedHeaders)}, " +
                         $"Signature={signature}";
        request.Headers.TryAddWithoutValidation("Authorization", authHeader);

        // Re-set the body content (it was consumed by ReadAsByteArrayAsync).
        // Preserve the original Content-Type if present.
        if (bodyBytes.Length > 0)
        {
            var contentType = request.Content?.Headers.ContentType;
            request.Content = new ByteArrayContent(bodyBytes);
            if (contentType != null)
                request.Content.Headers.ContentType = contentType;
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static List<string> BuildSignedHeaders(HttpRequestMessage request)
    {
        // Sign only the headers that MinIO expects: content-type, host, x-amz-* headers.
        // Content-Length must NOT be signed - it may be modified by transport layers.
        var headers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "host" };
        foreach (var h in request.Headers)
        {
            var key = h.Key.ToLowerInvariant();
            if (key.StartsWith("x-amz-", StringComparison.Ordinal))
                headers.Add(key);
        }

        if (request.Content?.Headers.ContentType != null)
            headers.Add("content-type");

        return [.. headers];
    }

    private static string BuildCanonicalHeaders(HttpRequestMessage request, List<string> signedHeaders)
    {
        var sb = new StringBuilder();
        foreach (var name in signedHeaders)
        {
            var values = name == "host"
                ? [request.RequestUri!.Authority]
                : request.Headers.TryGetValues(name, out var v) ? v.ToArray()
                : request.Content?.Headers.TryGetValues(name, out var cv) == true ? cv.ToArray()
                : [];
            sb.Append(name).Append(':').Append(string.Join(",", values.Select(v => v.Trim()))).Append('\n');
        }
        return sb.ToString();
    }

    private byte[] GetSigningKey(string dateStamp)
    {
        var kDate = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + _secretKey), dateStamp);
        var kRegion = HmacSha256(kDate, Region);
        var kService = HmacSha256(kRegion, Service);
        return HmacSha256(kService, "aws4_request");
    }

    private static byte[] HmacSha256(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string HexHash(byte[] data)
    {
        return HexEncode(SHA256.HashData(data));
    }

    private static string HexEncode(byte[] data)
    {
        return Convert.ToHexStringLower(data);
    }
}
