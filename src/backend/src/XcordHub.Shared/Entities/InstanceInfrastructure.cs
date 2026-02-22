namespace XcordHub.Entities;

public sealed class InstanceInfrastructure
{
    public long Id { get; set; }
    public long ManagedInstanceId { get; set; }
    public string DockerNetworkId { get; set; } = string.Empty;
    public string DockerContainerId { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string DatabasePassword { get; set; } = string.Empty;
    public int RedisDb { get; set; }
    public string MinioAccessKey { get; set; } = string.Empty;
    public string MinioSecretKey { get; set; } = string.Empty;
    public string CaddyRouteId { get; set; } = string.Empty;
    public string LiveKitApiKey { get; set; } = string.Empty;
    public string LiveKitSecretKey { get; set; } = string.Empty;
    public string? BootstrapTokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation properties
    public ManagedInstance ManagedInstance { get; set; } = null!;
}
