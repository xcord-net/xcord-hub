namespace XcordHub.Infrastructure.Options;

public sealed class MinioOptions
{
    public const string SectionName = "Storage";

    public string Endpoint { get; set; } = "minio:9000";

    /// <summary>Root MinIO access key (MINIO_ROOT_USER).</summary>
    public string AccessKey { get; set; } = "minioadmin";

    /// <summary>Root MinIO secret key (MINIO_ROOT_PASSWORD).</summary>
    public string SecretKey { get; set; } = "minioadmin";

    public bool UseSsl { get; set; } = false;
}
