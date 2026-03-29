using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using XcordHub.Entities;

namespace XcordHub.Features.Destruction;

/// <summary>
/// Removes the per-instance Redis ACL user created by ProvisionRedisAclStep.
/// If no ACL user was provisioned (empty RedisUsername), this step is a no-op.
/// Errors are logged as warnings and do not block the destruction pipeline.
/// </summary>
public sealed class RemoveRedisAclStep(
    IConnectionMultiplexer redis,
    ILogger<RemoveRedisAclStep> logger) : IDestructionStep
{
    public string StepName => "RemoveRedisAcl";

    public async Task ExecuteAsync(ManagedInstance instance, InstanceInfrastructure infrastructure, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(infrastructure.RedisUsername))
        {
            return;
        }

        logger.LogInformation("Removing Redis ACL user {Username} for instance {Domain}",
            infrastructure.RedisUsername, instance.Domain);

        try
        {
            var db = redis.GetDatabase();
            await db.ExecuteAsync("ACL", new object[] { "DELUSER", infrastructure.RedisUsername });
            logger.LogInformation("Removed Redis ACL user {Username}", infrastructure.RedisUsername);
        }
        catch (RedisException ex)
        {
            // Log as warning - ACL removal failure should not block destruction.
            logger.LogWarning(ex, "Failed to remove Redis ACL user {Username} for instance {Domain}",
                infrastructure.RedisUsername, instance.Domain);
        }
    }
}
