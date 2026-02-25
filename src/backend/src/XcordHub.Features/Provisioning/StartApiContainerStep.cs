using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using XcordHub;

namespace XcordHub.Features.Provisioning;

public sealed class StartApiContainerStep : IProvisioningStep
{
    private readonly HubDbContext _dbContext;
    private readonly IDockerService _dockerService;
    private readonly string _hubConnectionString;
    private readonly string _minioAccessKey;
    private readonly string _minioSecretKey;

    public string StepName => "StartApiContainer";

    public StartApiContainerStep(HubDbContext dbContext, IDockerService dockerService, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _dockerService = dockerService;
        _hubConnectionString = configuration.GetSection("Database:ConnectionString").Value
            ?? throw new InvalidOperationException("Database:ConnectionString not configured");
        // Instances use the hub's root MinIO credentials (per-instance MinIO users
        // are not yet provisioned).
        _minioAccessKey = configuration.GetSection("Storage:AccessKey").Value ?? "minioadmin";
        _minioSecretKey = configuration.GetSection("Storage:SecretKey").Value ?? "minioadmin";
    }

    public async Task<Result<bool>> ExecuteAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.ManagedInstances
            .Include(i => i.Infrastructure)
            .Include(i => i.Config)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance?.Infrastructure == null)
        {
            return Error.NotFound("INFRASTRUCTURE_NOT_FOUND", $"Infrastructure for instance {instanceId} not found");
        }

        try
        {
            // Generate config JSON in xcord-config.json format (read by xcord-fed entrypoint)
            var configJson = GenerateConfigJson(instance.Domain, instance.Infrastructure, instance.SnowflakeWorkerId, _hubConnectionString, _minioAccessKey, _minioSecretKey);

            // Resolve resource limits from InstanceConfig (set by EnforceTierLimitsStep)
            ContainerResourceLimits? resourceLimits = null;
            if (instance.Config?.ResourceLimitsJson != null)
            {
                var limits = JsonSerializer.Deserialize<ResourceLimits>(instance.Config.ResourceLimitsJson);
                if (limits != null)
                {
                    resourceLimits = new ContainerResourceLimits(
                        MemoryBytes: (long)limits.MaxMemoryMb * 1024 * 1024,
                        CpuQuota: (long)limits.MaxCpuPercent * 1000
                    );
                }
            }

            // Start container with config injected via XCORD_CONFIG_INLINE env var
            var containerId = await _dockerService.StartContainerAsync(instance.Domain, configJson, resourceLimits, cancellationToken);

            // Update infrastructure with container ID
            instance.Infrastructure.DockerContainerId = containerId;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            return Error.Failure("CONTAINER_START_FAILED", $"Failed to start container: {ex.Message}");
        }
    }

    public async Task<Result<bool>> VerifyAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var infrastructure = await _dbContext.InstanceInfrastructures
            .FirstOrDefaultAsync(i => i.ManagedInstanceId == instanceId, cancellationToken);

        if (infrastructure == null)
        {
            return Error.NotFound("INFRASTRUCTURE_NOT_FOUND", $"Infrastructure for instance {instanceId} not found");
        }

        if (string.IsNullOrWhiteSpace(infrastructure.DockerContainerId))
        {
            return Error.Failure("CONTAINER_ID_MISSING", "Container ID is missing");
        }

        try
        {
            var isRunning = await _dockerService.VerifyContainerRunningAsync(infrastructure.DockerContainerId, cancellationToken);
            return isRunning ? true : Error.Failure("CONTAINER_NOT_RUNNING", "Container is not running");
        }
        catch (Exception ex)
        {
            return Error.Failure("CONTAINER_VERIFY_ERROR", $"Container verification error: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates configuration JSON in the xcord-config.json format that
    /// xcord-fed/docker/entrypoint.sh reads from XCORD_CONFIG_INLINE.
    /// </summary>
    private static string GenerateConfigJson(
        string domain,
        Entities.InstanceInfrastructure infrastructure,
        long workerId,
        string hubConnectionString,
        string minioAccessKey,
        string minioSecretKey)
    {
        // Domain format for the instance: subdomain is used as-is (no suffix appended here —
        // the full domain (e.g. "test.localhost") is stored in ManagedInstance.Domain).
        var subdomain = domain.Split('.')[0];

        // Build instance DB connection using the same host/user/password as the hub DB
        // so we can connect to the newly created instance database. In production the
        // ProvisionDatabase step would create a per-instance DB user instead.
        var hubBuilder = new Npgsql.NpgsqlConnectionStringBuilder(hubConnectionString);
        var instanceConnStr = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = hubBuilder.Host,
            Port = hubBuilder.Port,
            Database = infrastructure.DatabaseName,
            Username = hubBuilder.Username,
            Password = hubBuilder.Password
        }.ConnectionString;

        var config = new
        {
            database = new
            {
                connectionString = instanceConnStr
            },
            redis = new
            {
                connectionString = "redis:6379",
                channelPrefix = $"{domain}:"
            },
            jwt = new
            {
                issuer = $"https://{domain}",
                audience = $"https://{domain}"
            },
            storage = new
            {
                endpoint = "http://minio:9000",
                accessKey = minioAccessKey,
                secretKey = minioSecretKey,
                bucket = $"xcord-{subdomain}",
                useSsl = false
            },
            livekit = new
            {
                host = "ws://livekit:7880",
                apiKey = infrastructure.LiveKitApiKey,
                apiSecret = infrastructure.LiveKitSecretKey
            },
            cors = new
            {
                allowedOrigins = new[] { $"https://{domain}" }
            },
            instance = new
            {
                domain = domain,
                name = $"Xcord Instance ({subdomain})"
            },
            snowflake = new
            {
                workerId = workerId
            },
            email = new
            {
                smtpHost = "mailpit",
                smtpPort = 1025,
                smtpUsername = "",
                smtpPassword = "",
                fromAddress = $"noreply@{domain}",
                fromName = "Xcord",
                useSsl = false,
                devMode = false
            },
            rateLimiting = new
            {
                maxRequests = 10000,
                windowSeconds = 60
            },
            gif = new
            {
                provider = "none",
                apiKey = ""
            },
            encryption = new
            {
                kek = infrastructure.InstanceKek
            },
            outbox = new
            {
                pollingIntervalMs = 500,
                batchSize = 100,
                cleanupIntervalMinutes = 60,
                retentionHours = 24
            }
        };

        return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = false });
    }
}
