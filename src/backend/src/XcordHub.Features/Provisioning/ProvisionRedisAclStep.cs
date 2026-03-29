using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using XcordHub.Infrastructure.Data;
using XcordHub;

namespace XcordHub.Features.Provisioning;

/// <summary>
/// Creates a per-instance Redis ACL user with key pattern restriction (~{prefix}:*).
/// The ACL user prevents a compromised instance from reading or writing keys outside
/// its own namespace. If Redis does not support ACL commands (e.g. older versions or
/// dev stacks without ACL configuration), ACL provisioning is skipped gracefully and
/// the instance falls back to using the default connection without per-user isolation.
/// </summary>
public sealed class ProvisionRedisAclStep : IProvisioningStep
{
    private readonly HubDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ProvisionRedisAclStep> _logger;

    public string StepName => "ProvisionRedisAcl";

    public ProvisionRedisAclStep(
        HubDbContext dbContext,
        IConnectionMultiplexer redis,
        ILogger<ProvisionRedisAclStep> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _logger = logger;
    }

    public async Task<Result<bool>> ExecuteAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.ManagedInstances
            .Include(i => i.Infrastructure)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance?.Infrastructure == null)
        {
            return Error.NotFound("INFRASTRUCTURE_NOT_FOUND", $"Infrastructure for instance {instanceId} not found");
        }

        var infra = instance.Infrastructure;

        // If already provisioned (idempotent resume), skip.
        if (!string.IsNullOrWhiteSpace(infra.RedisUsername))
        {
            _logger.LogInformation("Redis ACL user already provisioned for instance {Domain}, skipping", instance.Domain);
            return true;
        }

        var subdomain = ValidationHelpers.ExtractSubdomain(instance.Domain);
        // ACL username: xcord_{subdomain} - mirrors the DB username convention.
        // The prefix used for key isolation is the full domain (e.g. "test.xcord-dev.net:").
        var aclUsername = $"xcord_{subdomain}";
        var keyPrefix = $"{instance.Domain}:";
        var password = GenerateSecurePassword(32);

        try
        {
            var db = _redis.GetDatabase();

            // ACL SETUSER {username} on >{password} ~{prefix}* +@all
            // ~{prefix}* restricts key access to keys starting with this instance's channel prefix.
            // +@all grants all command categories (the key restriction is the isolation boundary).
            try
            {
                await db.ExecuteAsync("ACL", new object[]
                {
                    "SETUSER", aclUsername, "on", $">{password}", $"~{keyPrefix}*", "+@all"
                });
                _logger.LogInformation("Created Redis ACL user {Username} with key prefix {Prefix} for instance {Domain}",
                    aclUsername, keyPrefix, instance.Domain);
            }
            catch (RedisException ex) when (IsAclUnsupported(ex))
            {
                // Redis version < 6 or ACL not enabled - skip ACL provisioning gracefully.
                // The instance will connect without per-user isolation (dev/legacy setups only).
                _logger.LogWarning("Redis does not support ACL commands - skipping per-instance ACL provisioning for {Domain}: {Message}",
                    instance.Domain, ex.Message);

                // Leave RedisUsername empty to signal "no ACL configured".
                return true;
            }

            infra.RedisUsername = aclUsername;
            infra.RedisPassword = password;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (Exception ex) when (ex is not RedisException)
        {
            return Error.Failure("REDIS_ACL_FAILED", $"Failed to create Redis ACL user for instance '{instance.Domain}': {ex.Message}");
        }
    }

    public async Task<Result<bool>> VerifyAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var infra = await _dbContext.InstanceInfrastructures
            .FirstOrDefaultAsync(i => i.ManagedInstanceId == instanceId, cancellationToken);

        if (infra == null)
        {
            return Error.NotFound("INFRASTRUCTURE_NOT_FOUND", $"Infrastructure for instance {instanceId} not found");
        }

        // If RedisUsername is empty, ACL was skipped (unsupported Redis) - that's acceptable.
        if (string.IsNullOrWhiteSpace(infra.RedisUsername))
        {
            return true;
        }

        try
        {
            var db = _redis.GetDatabase();
            var result = await db.ExecuteAsync("ACL", new object[] { "GETUSER", infra.RedisUsername });
            if (result.IsNull)
            {
                return Error.Failure("REDIS_ACL_USER_NOT_FOUND",
                    $"Redis ACL user '{infra.RedisUsername}' not found after creation");
            }

            return true;
        }
        catch (RedisException ex) when (IsAclUnsupported(ex))
        {
            // ACL not supported - verification skipped.
            return true;
        }
        catch (Exception ex)
        {
            return Error.Failure("REDIS_ACL_VERIFY_FAILED", $"Redis ACL verification failed: {ex.Message}");
        }
    }

    private static bool IsAclUnsupported(RedisException ex)
    {
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("ERR unknown command", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("NOPERM", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("command not allowed", StringComparison.OrdinalIgnoreCase);
    }

    private static string GenerateSecurePassword(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(length);
        var result = new char[length];
        for (int i = 0; i < length; i++)
            result[i] = chars[bytes[i] % chars.Length];
        return new string(result);
    }
}
