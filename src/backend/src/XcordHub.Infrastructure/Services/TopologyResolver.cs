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

    public static string MapBillingTierToTopologyTier(
        FeatureTier featureTier,
        UserCountTier userCountTier)
    {
        if (userCountTier >= UserCountTier.Tier500)
            return "enterprise";

        return featureTier switch
        {
            FeatureTier.Chat => "free",
            FeatureTier.Audio => "basic",
            FeatureTier.Video => userCountTier >= UserCountTier.Tier100 ? "pro" : "basic",
            _ => "free"
        };
    }

    public List<string> GetPublicIpsForPool(string poolName)
    {
        return _topology.PublicIpsByPool.TryGetValue(poolName, out var ips) ? ips : [];
    }

    public string? GetDatabaseConnectionString(string placedInPool)
    {
        var pool = GetPoolByName(placedInPool);
        if (pool != null) return pool.Database.ConnectionString;
        var ded = GetDedicatedHostByPlacement(placedInPool);
        return ded?.Database.ConnectionString;
    }

    public PoolStorageConfig? GetStorageConfig(string placedInPool)
    {
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

    public string? GetRedisConnectionString(string placedInPool)
    {
        var pool = GetPoolByName(placedInPool);
        if (pool != null) return pool.Redis.ConnectionString;
        var ded = GetDedicatedHostByPlacement(placedInPool);
        return ded?.Redis.ConnectionString;
    }
}
