using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using XcordHub.Infrastructure.Options;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Provisions per-instance MinIO users, buckets, and IAM-style bucket policies
/// using the Minio .NET SDK (S3 operations) and the MinIO Console HTTP API
/// (user/policy management, which is not exposed by the S3-compatible API).
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

        // 2. Use the Console API to create user and policy (best-effort)
        try
        {
            await ProvisionConsoleResourcesAsync(bucketName, accessKey, secretKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MinIO Console API provisioning failed for bucket {Bucket} / user {AccessKey}. " +
                "Bucket was created but per-instance user was not configured. " +
                "The instance will fall back to root credentials.",
                bucketName, accessKey);
        }

        _logger.LogInformation("MinIO bucket {Bucket} provisioned for user {AccessKey}", bucketName, accessKey);
    }

    public async Task DeprovisionBucketAsync(
        string bucketName,
        string accessKey,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deprovisioning MinIO bucket {Bucket} for user {AccessKey}", bucketName, accessKey);

        // Best-effort cleanup — log and continue on each step
        // 1. Remove Console API resources (user + policy) first
        try
        {
            await DeprovisionConsoleResourcesAsync(bucketName, accessKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MinIO Console API deprovisioning failed for user {AccessKey} — continuing bucket cleanup",
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

            // ListObjects requires s3:ListBucket — will fail with 403 for invalid credentials
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

    private async Task EmptyAndRemoveBucketAsync(string bucketName, CancellationToken cancellationToken)
    {
        var exists = await _rootClient.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(bucketName), cancellationToken);

        if (!exists)
        {
            _logger.LogDebug("MinIO bucket {Bucket} not found during cleanup — skipping", bucketName);
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
    // MinIO Console API operations (user + policy management)
    // -------------------------------------------------------------------------

    private async Task ProvisionConsoleResourcesAsync(
        string bucketName,
        string accessKey,
        string secretKey,
        CancellationToken cancellationToken)
    {
        using var http = _httpClientFactory.CreateClient("MinioConsole");

        var sessionId = await LoginConsoleAsync(http, cancellationToken);

        // Create user
        await CreateConsoleUserAsync(http, sessionId, accessKey, secretKey, cancellationToken);

        // Create policy scoped to this bucket
        var policyName = $"xcord-policy-{accessKey.ToLowerInvariant()}";
        var policyDoc = BuildBucketPolicy(bucketName);
        await CreateConsolePolicyAsync(http, sessionId, policyName, policyDoc, cancellationToken);

        // Attach policy to user
        await AttachConsolePolicyAsync(http, sessionId, accessKey, policyName, cancellationToken);
    }

    private async Task DeprovisionConsoleResourcesAsync(
        string bucketName,
        string accessKey,
        CancellationToken cancellationToken)
    {
        using var http = _httpClientFactory.CreateClient("MinioConsole");

        var sessionId = await LoginConsoleAsync(http, cancellationToken);

        // Delete user (MinIO automatically detaches their policies)
        await DeleteConsoleUserAsync(http, sessionId, accessKey, cancellationToken);

        // Delete policy
        var policyName = $"xcord-policy-{accessKey.ToLowerInvariant()}";
        await DeleteConsolePolicyAsync(http, sessionId, policyName, cancellationToken);
    }

    private async Task<string> LoginConsoleAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var loginPayload = new { accessKey = _options.AccessKey, secretKey = _options.SecretKey };

        var response = await http.PostAsJsonAsync("/api/v1/login", loginPayload, cancellationToken);
        response.EnsureSuccessStatusCode();

        // MinIO Console returns 204 No Content with the session token in a Set-Cookie header
        // e.g. Set-Cookie: token=<jwt>; Path=/; ...
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
            throw new InvalidOperationException("MinIO Console login did not return a Set-Cookie header");

        foreach (var cookie in cookies)
        {
            if (cookie.StartsWith("token=", StringComparison.OrdinalIgnoreCase))
            {
                var endIndex = cookie.IndexOf(';');
                var token = endIndex > 0 ? cookie[6..endIndex] : cookie[6..];
                return token;
            }
        }

        throw new InvalidOperationException("MinIO Console login Set-Cookie did not contain a token");
    }

    private static async Task CreateConsoleUserAsync(
        HttpClient http,
        string sessionId,
        string accessKey,
        string secretKey,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users")
        {
            Content = JsonContent.Create(new { accessKey, secretKey }),
            Headers = { { "Cookie", $"token={sessionId}" } }
        };

        var response = await http.SendAsync(request, cancellationToken);

        // 409 Conflict means user already exists — treat as success
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            return;

        response.EnsureSuccessStatusCode();
    }

    private static async Task CreateConsolePolicyAsync(
        HttpClient http,
        string sessionId,
        string policyName,
        string policyDocument,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/policies")
        {
            Content = JsonContent.Create(new { name = policyName, policy = policyDocument }),
            Headers = { { "Cookie", $"token={sessionId}" } }
        };

        var response = await http.SendAsync(request, cancellationToken);

        // 409 Conflict means policy already exists — treat as success
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            return;

        response.EnsureSuccessStatusCode();
    }

    private static async Task AttachConsolePolicyAsync(
        HttpClient http,
        string sessionId,
        string accessKey,
        string policyName,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/user/{Uri.EscapeDataString(accessKey)}/policies")
        {
            Content = JsonContent.Create(new { policies = new[] { policyName } }),
            Headers = { { "Cookie", $"token={sessionId}" } }
        };

        var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task DeleteConsoleUserAsync(
        HttpClient http,
        string sessionId,
        string accessKey,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/users/{Uri.EscapeDataString(accessKey)}")
        {
            Headers = { { "Cookie", $"token={sessionId}" } }
        };

        var response = await http.SendAsync(request, cancellationToken);

        // 404 = already deleted — treat as success
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return;

        response.EnsureSuccessStatusCode();
    }

    private static async Task DeleteConsolePolicyAsync(
        HttpClient http,
        string sessionId,
        string policyName,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/policies/{Uri.EscapeDataString(policyName)}")
        {
            Headers = { { "Cookie", $"token={sessionId}" } }
        };

        var response = await http.SendAsync(request, cancellationToken);

        // 404 = already deleted — treat as success
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return;

        response.EnsureSuccessStatusCode();
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
}
