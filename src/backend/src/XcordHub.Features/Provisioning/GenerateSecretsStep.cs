using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub;

namespace XcordHub.Features.Provisioning;

public sealed class GenerateSecretsStep : IProvisioningStep
{
    private readonly HubDbContext _dbContext;
    private readonly SnowflakeId _snowflakeGenerator;

    public string StepName => "GenerateSecrets";

    public GenerateSecretsStep(HubDbContext dbContext, SnowflakeId snowflakeGenerator)
    {
        _dbContext = dbContext;
        _snowflakeGenerator = snowflakeGenerator;
    }

    public async Task<Result<bool>> ExecuteAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.ManagedInstances
            .Include(i => i.Infrastructure)
            .Include(i => i.Secrets)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance == null)
        {
            return Error.NotFound("INSTANCE_NOT_FOUND", $"Instance {instanceId} not found");
        }

        // Skip if infrastructure already exists
        if (instance.Infrastructure != null)
        {
            return true;
        }

        // Generate secrets
        var dbPassword = GenerateSecurePassword(32);
        var minioAccessKey = GenerateAccessKey(20);
        var minioSecretKey = GenerateSecurePassword(40);
        var liveKitApiKey = GenerateAccessKey(20);
        var liveKitSecretKey = GenerateSecurePassword(40);
        var bootstrapToken = GenerateBootstrapToken();

        var infrastructure = new InstanceInfrastructure
        {
            Id = _snowflakeGenerator.NextId(),
            ManagedInstanceId = instanceId,
            DockerContainerId = string.Empty, // Will be set in StartApiContainer step
            DatabaseName = $"xcord_{instance.Domain.Replace("-", "_").Replace(".", "_")}",
            DatabasePassword = dbPassword,
            RedisDb = 0, // Will be allocated later
            MinioAccessKey = minioAccessKey,
            MinioSecretKey = minioSecretKey,
            CaddyRouteId = string.Empty, // Will be set in ConfigureDnsAndProxy step
            LiveKitApiKey = liveKitApiKey,
            LiveKitSecretKey = liveKitSecretKey,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.InstanceInfrastructures.Add(infrastructure);

        // Store bootstrap token hash in InstanceSecrets
        if (instance.Secrets != null)
        {
            instance.Secrets.BootstrapTokenHash = HashToken(bootstrapToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<Result<bool>> VerifyAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var infrastructure = await _dbContext.InstanceInfrastructures
            .FirstOrDefaultAsync(i => i.ManagedInstanceId == instanceId, cancellationToken);

        if (infrastructure == null)
        {
            return Error.Failure("SECRETS_MISSING", "Infrastructure secrets not found");
        }

        if (string.IsNullOrWhiteSpace(infrastructure.DatabasePassword) ||
            string.IsNullOrWhiteSpace(infrastructure.MinioAccessKey) ||
            string.IsNullOrWhiteSpace(infrastructure.MinioSecretKey))
        {
            return Error.Failure("SECRETS_INCOMPLETE", "Infrastructure secrets are incomplete");
        }

        return true;
    }

    private static string GenerateSecurePassword(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);

        var result = new char[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = chars[bytes[i] % chars.Length];
        }

        return new string(result);
    }

    private static string GenerateAccessKey(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);

        var result = new char[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = chars[bytes[i] % chars.Length];
        }

        return new string(result);
    }

    private static string GenerateBootstrapToken()
    {
        var randomBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    private static string HashToken(string token)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hashBytes);
    }
}
