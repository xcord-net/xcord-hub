using Microsoft.Extensions.Options;
using XcordHub.Entities;
using XcordHub.Infrastructure.Options;

namespace XcordHub.Infrastructure.Services;

public sealed class TopologyResolver
{
    private readonly TopologyOptions _topology;

    public TopologyResolver(IOptions<TopologyOptions> topology)
    {
        _topology = topology.Value;
    }

    public bool IsConfigured => _topology.IsConfigured;

    public ComputePoolConfig? FindPoolForTier(string tier)
    {
        if (!_topology.IsConfigured) return null;
        return _topology.ComputePools.FirstOrDefault(p =>
            string.Equals(p.Tier, tier, StringComparison.OrdinalIgnoreCase));
    }

    public DataPoolConfig? FindDataPool(string name)
    {
        return _topology.DataPools.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public DataPoolConfig? GetDataPoolByName(string placedInDataPool)
    {
        if (string.IsNullOrEmpty(placedInDataPool)) return null;
        return FindDataPool(placedInDataPool);
    }

    public DataPoolConfig? FindDataPoolForPool(string computePoolName)
    {
        if (_topology.DataPools.Count == 0) return null;
        // If there's exactly one data pool, use it (common case)
        if (_topology.DataPools.Count == 1) return _topology.DataPools[0];
        // Otherwise, match by name
        return FindDataPool(computePoolName);
    }

    public DedicatedHostConfig? FindDedicatedHost(string hostId)
    {
        return _topology.DedicatedHosts.FirstOrDefault(h =>
            string.Equals(h.Id, hostId, StringComparison.OrdinalIgnoreCase));
    }

    public ComputePoolConfig? GetPoolByName(string placedInPool)
    {
        if (placedInPool == "default") return null;
        if (placedInPool.StartsWith("dedicated:")) return null;
        return _topology.ComputePools.FirstOrDefault(p =>
            string.Equals(p.Name, placedInPool, StringComparison.OrdinalIgnoreCase));
    }

    public DedicatedHostConfig? GetDedicatedHostByPlacement(string placedInPool)
    {
        if (!placedInPool.StartsWith("dedicated:")) return null;
        var hostId = placedInPool["dedicated:".Length..];
        return FindDedicatedHost(hostId);
    }

    public string ResolvePoolName(InstanceTier tier)
    {
        return tier switch
        {
            InstanceTier.Free => "free",
            InstanceTier.Basic => "basic",
            InstanceTier.Pro => "pro",
            InstanceTier.Enterprise => "enterprise",
            _ => "basic"
        };
    }

    public List<string> GetPublicIpsForPool(string poolName)
    {
        return _topology.PublicIpsByPool.TryGetValue(poolName, out var ips) ? ips : [];
    }

    public string? GetDatabaseConnectionString(string placedInPool, string? placedInDataPool = null)
    {
        var dataPool = GetDataPoolByName(placedInDataPool ?? "");
        if (dataPool != null) return dataPool.Database.ConnectionString;
        var pool = GetPoolByName(placedInPool);
        if (pool != null) return pool.Database.ConnectionString;
        var ded = GetDedicatedHostByPlacement(placedInPool);
        return ded?.Database.ConnectionString;
    }

    public PoolStorageConfig? GetStorageConfig(string placedInPool, string? placedInDataPool = null)
    {
        var dataPool = GetDataPoolByName(placedInDataPool ?? "");
        if (dataPool != null) return dataPool.Storage;
        var pool = GetPoolByName(placedInPool);
        if (pool != null) return pool.Storage;
        var ded = GetDedicatedHostByPlacement(placedInPool);
        return ded?.Storage;
    }

    public string? GetDockerSocketProxyUrl(string placedInPool)
    {
        var pool = GetPoolByName(placedInPool);
        if (pool != null) return pool.Docker.SocketProxyUrl;
        var ded = GetDedicatedHostByPlacement(placedInPool);
        return ded?.Docker.SocketProxyUrl;
    }

    public string? GetCaddyAdminUrl(string placedInPool)
    {
        var pool = GetPoolByName(placedInPool);
        if (pool != null) return pool.Caddy.AdminUrl;
        var ded = GetDedicatedHostByPlacement(placedInPool);
        return ded?.Caddy.AdminUrl;
    }

    public PoolLiveKitConfig? GetLiveKitConfig(string placedInPool)
    {
        var pool = GetPoolByName(placedInPool);
        if (pool != null) return pool.LiveKit;
        var ded = GetDedicatedHostByPlacement(placedInPool);
        return ded?.LiveKit;
    }

    public string? GetRedisConnectionString(string placedInPool, string? placedInDataPool = null)
    {
        var dataPool = GetDataPoolByName(placedInDataPool ?? "");
        if (dataPool != null) return dataPool.Redis.ConnectionString;
        var pool = GetPoolByName(placedInPool);
        if (pool != null) return pool.Redis.ConnectionString;
        var ded = GetDedicatedHostByPlacement(placedInPool);
        return ded?.Redis.ConnectionString;
    }

    public string? GetInstanceImageForPool(string placedInPool)
    {
        var pool = GetPoolByName(placedInPool);
        if (pool != null) return pool.Docker.InstanceImage;
        var ded = GetDedicatedHostByPlacement(placedInPool);
        return ded?.Docker.InstanceImage;
    }

    /// <summary>
    /// Returns the Docker overlay network name for the pool that the instance is placed in.
    /// Returns null for dev/default placement (caller falls back to xcord-shared-net).
    /// </summary>
    public string? GetPoolNetworkName(string placedInPool)
    {
        var pool = GetPoolByName(placedInPool);
        if (pool != null && !string.IsNullOrWhiteSpace(pool.Docker.PoolNetworkName))
            return pool.Docker.PoolNetworkName;
        var ded = GetDedicatedHostByPlacement(placedInPool);
        if (ded != null && !string.IsNullOrWhiteSpace(ded.Docker.PoolNetworkName))
            return ded.Docker.PoolNetworkName;
        return null;
    }
}
