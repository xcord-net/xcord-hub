namespace XcordHub.Infrastructure.Options;

public sealed class MinioOptions
{
    public const string SectionName = "Storage";

    public string Endpoint { get; set; } = "minio:9000";

    /// <summary>
    /// MinIO Console API endpoint (used for user/policy management via the MinIO web console HTTP API).
    /// Defaults to port 9001 which is the standard MinIO console port.
    /// </summary>
    public string ConsoleEndpoint { get; set; } = "minio:9001";

    /// <summary>Root MinIO access key (MINIO_ROOT_USER).</summary>
    public string AccessKey { get; set; } = "minioadmin";

    /// <summary>Root MinIO secret key (MINIO_ROOT_PASSWORD).</summary>
    public string SecretKey { get; set; } = "minioadmin";

    public bool UseSsl { get; set; } = false;
}
