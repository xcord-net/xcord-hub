using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using XcordHub.Entities;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Destruction;

/// <summary>
/// Flushes all keys for the instance's Redis DB during destruction.
/// Each xcord-fed instance namespaces its Redis keys with its domain prefix
/// (e.g. "myinstance.xcord.net:*"), so we issue a targeted SCAN + DEL sweep
/// rather than FLUSHDB to avoid evicting unrelated data on shared Redis instances.
///
/// When a dedicated Redis DB number is allocated per instance (RedisDb > 0), the step
/// also records that the slot is available again by flushing the entire DB. Until full
/// Redis DB slot allocation is implemented in provisioning, this step performs the
/// prefix-based sweep on DB 0 (the default).
/// </summary>
public sealed class ReleaseRedisSlotStep : IDestructionStep
{
    private readonly string _hubRedisConnectionString;
    private readonly TopologyResolver _resolver;
    private readonly ILogger<ReleaseRedisSlotStep> _logger;

    public string StepName => "ReleaseRedisSlot";

    public ReleaseRedisSlotStep(IConfiguration configuration, TopologyResolver resolver, ILogger<ReleaseRedisSlotStep> logger)
    {
        _hubRedisConnectionString = configuration.GetSection("Redis:ConnectionString").Value
            ?? throw new InvalidOperationException("Redis:ConnectionString not configured");
        _resolver = resolver;
        _logger = logger;
    }

    public async Task ExecuteAsync(ManagedInstance instance, InstanceInfrastructure infrastructure, CancellationToken cancellationToken)
    {
        try
        {
            var poolRedisConnStr = _resolver.GetRedisConnectionString(infrastructure.PlacedInPool, infrastructure.PlacedInDataPool);
            var redisConnectionString = poolRedisConnStr ?? _hubRedisConnectionString;

            var configOptions = ConfigurationOptions.Parse(redisConnectionString);
            configOptions.AbortOnConnectFail = false;
            configOptions.ConnectTimeout = 5000;
            configOptions.SyncTimeout = 5000;

            await using var redis = await ConnectionMultiplexer.ConnectAsync(configOptions);
            var redisDb = redis.GetDatabase(infrastructure.RedisDb);
            var server = redis.GetServer(redis.GetEndPoints()[0]);

            // Each xcord-fed instance uses its domain as a Redis key prefix.
            // Sweep all keys matching that prefix and delete them in batches.
            var prefix = $"{instance.Domain}:";
            _logger.LogInformation(
                "Sweeping Redis keys with prefix {Prefix} on DB {RedisDb} for instance {Domain}",
                prefix, infrastructure.RedisDb, instance.Domain);

            var deletedCount = 0;
            var batch = new List<RedisKey>(64);

            await foreach (var key in server.KeysAsync(infrastructure.RedisDb, pattern: $"{prefix}*", pageSize: 100).WithCancellation(cancellationToken))
            {
                batch.Add(key);
                if (batch.Count >= 64)
                {
                    deletedCount += (int)await redisDb.KeyDeleteAsync(batch.ToArray());
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
                deletedCount += (int)await redisDb.KeyDeleteAsync(batch.ToArray());

            _logger.LogInformation(
                "Deleted {Count} Redis keys for instance {Domain} (prefix: {Prefix})",
                deletedCount, instance.Domain, prefix);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReleaseRedisSlot step failed for instance {Domain} - continuing cleanup", instance.Domain);
        }
    }
}
