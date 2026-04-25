using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using XcordHub.Infrastructure.Options;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Provisions per-instance MinIO users, buckets, and IAM-style bucket policies
/// using the Minio .NET SDK (S3 operations) and the MinIO Admin REST API
/// (/minio/admin/v3/ endpoints with SigV4 auth and DARE body encryption).
/// </summary>
public sealed class MinioProvisioningService : IMinioProvisioningService
{
    private readonly IMinioClient _rootClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MinioOptions _options;
    private readonly ILogger<MinioProvisioningService> _logger;

    public MinioProvisioningService(
        IMinioClient rootClient,
        IHttpClientFactory httpClientFactory,
        IOptions<MinioOptions> options,
        ILogger<MinioProvisioningService> logger)
    {
        _rootClient = rootClient;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public async Task ProvisionBucketAsync(
        string bucketName,
        string accessKey,
        string secretKey,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Provisioning MinIO bucket {Bucket} for user {AccessKey}", bucketName, accessKey);

        // 1. Create the bucket (idempotent)
        await EnsureBucketExistsAsync(bucketName, cancellationToken);

        // 2. Apply explicit deny-anonymous bucket policy (defense in depth on top of IAM).
        await ApplyDenyAnonymousBucketPolicyAsync(bucketName, cancellationToken);

        // 3. Create IAM user and bucket-scoped policy via Admin API
        await ProvisionAdminResourcesAsync(bucketName, accessKey, secretKey, cancellationToken);

        _logger.LogInformation("MinIO bucket {Bucket} provisioned for user {AccessKey}", bucketName, accessKey);
    }

    public async Task DeprovisionBucketAsync(
        string bucketName,
        string accessKey,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deprovisioning MinIO bucket {Bucket} for user {AccessKey}", bucketName, accessKey);

        // Best-effort cleanup - log and continue on each step
        // 1. Remove IAM user and policy
        try
        {
            await DeprovisionAdminResourcesAsync(bucketName, accessKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MinIO Admin API deprovisioning failed for user {AccessKey} - continuing bucket cleanup",
                accessKey);
        }

        // 2. Empty and remove the bucket
        try
        {
            await EmptyAndRemoveBucketAsync(bucketName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to empty/remove MinIO bucket {Bucket}", bucketName);
        }

        _logger.LogInformation("MinIO deprovisioning complete for bucket {Bucket} / user {AccessKey}", bucketName, accessKey);
    }

    public async Task<bool> VerifyBucketAsync(
        string bucketName,
        string accessKey,
        string secretKey,
        CancellationToken cancellationToken = default)
    {
        // Verify the bucket exists (using root credentials)
        try
        {
            var exists = await _rootClient.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(bucketName), cancellationToken);

            if (!exists)
            {
                _logger.LogWarning("MinIO bucket {Bucket} does not exist during verification", bucketName);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MinIO bucket verification failed for {Bucket}", bucketName);
            return false;
        }

        // Verify per-instance credentials can actually access the bucket.
        // BucketExistsAsync (HEAD) returns true for 403 (bucket exists but access denied),
        // so we use ListObjectsAsync which requires real read permission.
        try
        {
            var endpoint = _options.Endpoint;
            var useHttps = _options.UseSsl;

            // Strip protocol prefix if present
            if (endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                endpoint = endpoint[7..];
            else if (endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                endpoint = endpoint[8..];

            var instanceClient = new MinioClient()
                .WithEndpoint(endpoint)
                .WithCredentials(accessKey, secretKey)
                .WithSSL(useHttps)
                .Build();

            // ListObjects requires s3:ListBucket - will fail with 403 for invalid credentials
            var listArgs = new ListObjectsArgs()
                .WithBucket(bucketName)
                .WithRecursive(false)
                .WithPrefix("__verify__");

            await foreach (var _ in instanceClient.ListObjectsEnumAsync(listArgs, cancellationToken))
            {
                break; // We only need the request to succeed, not actual objects
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Per-instance MinIO credentials for {AccessKey} / bucket {Bucket} failed verification",
                accessKey, bucketName);
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // S3 bucket operations (Minio SDK)
    // -------------------------------------------------------------------------

    private async Task EnsureBucketExistsAsync(string bucketName, CancellationToken cancellationToken)
    {
        var exists = await _rootClient.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(bucketName), cancellationToken);

        if (!exists)
        {
            _logger.LogInformation("Creating MinIO bucket {Bucket}", bucketName);
            await _rootClient.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(bucketName), cancellationToken);
            _logger.LogInformation("Created MinIO bucket {Bucket}", bucketName);
        }
        else
        {
            _logger.LogDebug("MinIO bucket {Bucket} already exists", bucketName);
        }
    }

    private async Task ApplyDenyAnonymousBucketPolicyAsync(string bucketName, CancellationToken cancellationToken)
    {
        var policy = BuildDenyAnonymousBucketPolicy(bucketName);
        try
        {
            var args = new SetPolicyArgs()
                .WithBucket(bucketName)
                .WithPolicy(policy);
            await _rootClient.SetPolicyAsync(args, cancellationToken);
            _logger.LogInformation("Applied deny-anonymous bucket policy to {Bucket}", bucketName);
        }
        catch (Exception ex)
        {
            // If the SDK/server rejects the policy, fail provisioning - do not silently leave
            // the bucket without an explicit anonymous-deny policy.
            _logger.LogError(ex, "Failed to apply deny-anonymous bucket policy to {Bucket}", bucketName);
            throw;
        }
    }

    private async Task EmptyAndRemoveBucketAsync(string bucketName, CancellationToken cancellationToken)
    {
        var exists = await _rootClient.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(bucketName), cancellationToken);

        if (!exists)
        {
            _logger.LogDebug("MinIO bucket {Bucket} not found during cleanup - skipping", bucketName);
            return;
        }

        _logger.LogInformation("Removing MinIO bucket {Bucket}", bucketName);

        // List and delete all objects first (bucket must be empty before removal)
        var listArgs = new ListObjectsArgs()
            .WithBucket(bucketName)
            .WithRecursive(true);

        var objectsToDelete = new List<string>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await foreach (var item in _rootClient.ListObjectsEnumAsync(listArgs, cts.Token))
            {
                objectsToDelete.Add(item.Key);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Timed out listing objects in bucket {Bucket} for cleanup", bucketName);
        }

        foreach (var objectKey in objectsToDelete)
        {
            try
            {
                await _rootClient.RemoveObjectAsync(
                    new RemoveObjectArgs().WithBucket(bucketName).WithObject(objectKey),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete object {Object} from bucket {Bucket}", objectKey, bucketName);
            }
        }

        await _rootClient.RemoveBucketAsync(
            new RemoveBucketArgs().WithBucket(bucketName), cancellationToken);

        _logger.LogInformation("Removed MinIO bucket {Bucket}", bucketName);
    }

    // -------------------------------------------------------------------------
    // MinIO Admin REST API operations (/minio/admin/v3/)
    // -------------------------------------------------------------------------

    private async Task ProvisionAdminResourcesAsync(
        string bucketName,
        string accessKey,
        string secretKey,
        CancellationToken cancellationToken)
    {
        using var http = _httpClientFactory.CreateClient("MinioAdmin");

        // 1. Create IAM user (encrypted body - contains secret key)
        await AddUserAsync(http, accessKey, secretKey, cancellationToken);

        // 2. Create bucket-scoped canned policy (plain body - not secret)
        var policyName = $"xcord-policy-{accessKey.ToLowerInvariant()}";
        var policyDoc = BuildBucketPolicy(bucketName);
        await AddCannedPolicyAsync(http, policyName, policyDoc, cancellationToken);

        // 3. Attach policy to user (encrypted body)
        await AttachPolicyAsync(http, accessKey, policyName, cancellationToken);
    }

    private async Task DeprovisionAdminResourcesAsync(
        string bucketName,
        string accessKey,
        CancellationToken cancellationToken)
    {
        using var http = _httpClientFactory.CreateClient("MinioAdmin");

        // Delete user (MinIO automatically detaches their policies)
        await RemoveUserAsync(http, accessKey, cancellationToken);

        // Delete policy
        var policyName = $"xcord-policy-{accessKey.ToLowerInvariant()}";
        await RemoveCannedPolicyAsync(http, policyName, cancellationToken);
    }

    /// <summary>
    /// PUT /minio/admin/v3/add-user?accessKey={key}
    /// Body: DARE-encrypted JSON {"secretKey":"...","status":"enabled"}
    /// </summary>
    private async Task AddUserAsync(HttpClient http, string accessKey, string secretKey,
        CancellationToken cancellationToken)
    {
        var userInfo = JsonSerializer.Serialize(new { secretKey, status = "enabled" });
        var encrypted = MinioAdminCrypto.EncryptData(_options.SecretKey, Encoding.UTF8.GetBytes(userInfo));

        var request = new HttpRequestMessage(HttpMethod.Put,
            $"/minio/admin/v3/add-user?accessKey={Uri.EscapeDataString(accessKey)}")
        {
            Content = CreateOctetContent(encrypted)
        };

        var response = await http.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            _logger.LogDebug("MinIO user {AccessKey} already exists", accessKey);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("MinIO add-user failed: {StatusCode} {Body}", response.StatusCode, body);
        }

        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Created MinIO user {AccessKey}", accessKey);
    }

    /// <summary>
    /// PUT /minio/admin/v3/add-canned-policy?name={policyName}
    /// Body: plain JSON policy document (not encrypted)
    /// </summary>
    private async Task AddCannedPolicyAsync(HttpClient http, string policyName, string policyDocument,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put,
            $"/minio/admin/v3/add-canned-policy?name={Uri.EscapeDataString(policyName)}")
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(policyDocument))
        };

        var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Created MinIO policy {PolicyName}", policyName);
    }

    /// <summary>
    /// POST /minio/admin/v3/idp/builtin/policy/attach
    /// Body: DARE-encrypted JSON {"policies":["..."],"user":"..."}
    /// </summary>
    private async Task AttachPolicyAsync(HttpClient http, string accessKey, string policyName,
        CancellationToken cancellationToken)
    {
        var attachReq = JsonSerializer.Serialize(new { policies = new[] { policyName }, user = accessKey });
        var encrypted = MinioAdminCrypto.EncryptData(_options.SecretKey, Encoding.UTF8.GetBytes(attachReq));

        var request = new HttpRequestMessage(HttpMethod.Post,
            "/minio/admin/v3/idp/builtin/policy/attach")
        {
            Content = CreateOctetContent(encrypted)
        };

        var response = await http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("MinIO attach-policy failed: {StatusCode} {Body}", response.StatusCode, body);
        }

        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Attached policy {PolicyName} to user {AccessKey}", policyName, accessKey);
    }

    /// <summary>
    /// DELETE /minio/admin/v3/remove-user?accessKey={key}
    /// No body required.
    /// </summary>
    private async Task RemoveUserAsync(HttpClient http, string accessKey,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete,
            $"/minio/admin/v3/remove-user?accessKey={Uri.EscapeDataString(accessKey)}");

        var response = await http.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("MinIO user {AccessKey} not found during cleanup", accessKey);
            return;
        }

        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Removed MinIO user {AccessKey}", accessKey);
    }

    /// <summary>
    /// DELETE /minio/admin/v3/delete-canned-policy?name={policyName}
    /// No body required.
    /// </summary>
    private async Task RemoveCannedPolicyAsync(HttpClient http, string policyName,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete,
            $"/minio/admin/v3/delete-canned-policy?name={Uri.EscapeDataString(policyName)}");

        var response = await http.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("MinIO policy {PolicyName} not found during cleanup", policyName);
            return;
        }

        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Removed MinIO policy {PolicyName}", policyName);
    }

    // -------------------------------------------------------------------------
    // Policy document builder
    // -------------------------------------------------------------------------

    private static string BuildBucketPolicy(string bucketName)
    {
        var policy = new
        {
            Version = "2012-10-17",
            Statement = new[]
            {
                new
                {
                    Effect = "Allow",
                    Action = new[] { "s3:*" },
                    Resource = new[]
                    {
                        $"arn:aws:s3:::{bucketName}",
                        $"arn:aws:s3:::{bucketName}/*"
                    }
                }
            }
        };

        return JsonSerializer.Serialize(policy);
    }

    /// <summary>
    /// Bucket-level policy that explicitly denies any action by anonymous principals.
    /// Authenticated IAM users are unaffected (their access is granted by their canned policy).
    /// </summary>
    private static string BuildDenyAnonymousBucketPolicy(string bucketName)
    {
        var policy = new
        {
            Version = "2012-10-17",
            Statement = new[]
            {
                new
                {
                    Sid = "DenyAnonymousAccess",
                    Effect = "Deny",
                    Principal = new { AWS = new[] { "*" } },
                    Action = new[] { "s3:*" },
                    Resource = new[]
                    {
                        $"arn:aws:s3:::{bucketName}",
                        $"arn:aws:s3:::{bucketName}/*"
                    },
                    Condition = new
                    {
                        StringEquals = new Dictionary<string, string>
                        {
                            ["aws:PrincipalType"] = "Anonymous"
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(policy);
    }

    private static ByteArrayContent CreateOctetContent(byte[] data)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }
}
