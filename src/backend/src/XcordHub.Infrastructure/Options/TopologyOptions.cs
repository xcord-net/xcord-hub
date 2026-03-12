namespace XcordHub.Infrastructure.Options;

public sealed class TopologyOptions
{
    public const string SectionName = "Topology";
    public List<ComputePoolConfig> ComputePools { get; set; } = [];
    public List<DataPoolConfig> DataPools { get; set; } = [];
    public List<DedicatedHostConfig> DedicatedHosts { get; set; } = [];
    public DnsConfig? Dns { get; set; }
    public Dictionary<string, List<string>> PublicIpsByPool { get; set; } = new();
    public bool IsConfigured => ComputePools.Count > 0 || DedicatedHosts.Count > 0;
}

public sealed class ComputePoolConfig
{
    public string Name { get; set; } = string.Empty;
    public string Tier { get; set; } = "free";
    public PoolDatabaseConfig Database { get; set; } = new();
    public PoolRedisConfig Redis { get; set; } = new();
    public PoolStorageConfig Storage { get; set; } = new();
    public PoolDockerConfig Docker { get; set; } = new();
    public PoolCaddyConfig Caddy { get; set; } = new();
    public PoolLiveKitConfig LiveKit { get; set; } = new();
    public PoolCapacityConfig Capacity { get; set; } = new();
}

public sealed class DataPoolConfig
{
    public string Name { get; set; } = string.Empty;
    public PoolDatabaseConfig Database { get; set; } = new();
    public PoolRedisConfig Redis { get; set; } = new();
    public PoolStorageConfig Storage { get; set; } = new();
    public PoolCapacityConfig Capacity { get; set; } = new();
}

public sealed class DedicatedHostConfig
{
    public string Id { get; set; } = string.Empty;
    public string Tier { get; set; } = "enterprise";
    public PoolDatabaseConfig Database { get; set; } = new();
    public PoolRedisConfig Redis { get; set; } = new();
    public PoolStorageConfig Storage { get; set; } = new();
    public PoolDockerConfig Docker { get; set; } = new();
    public PoolCaddyConfig Caddy { get; set; } = new();
    public PoolLiveKitConfig LiveKit { get; set; } = new();
}

public sealed class DnsConfig
{
    public string Provider { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public string BaseDomain { get; set; } = string.Empty;
}

public sealed class PoolDatabaseConfig
{
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class PoolRedisConfig
{
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class PoolStorageConfig
{
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool UseSsl { get; set; }
}

public sealed class PoolDockerConfig
{
    public string SocketProxyUrl { get; set; } = string.Empty;
    public string InstanceImage { get; set; } = "ghcr.io/xcord/fed:latest";
}

public sealed class PoolCaddyConfig
{
    public string AdminUrl { get; set; } = string.Empty;
}

public sealed class PoolLiveKitConfig
{
    public string Host { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
}

public sealed class PoolCapacityConfig
{
    public int TenantSlots { get; set; }
    public int MemoryMbPerTenant { get; set; } = 256;
    public int CpuMillicoresPerTenant { get; set; } = 250;
}
