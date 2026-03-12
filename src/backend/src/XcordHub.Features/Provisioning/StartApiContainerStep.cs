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
    private readonly TopologyResolver _resolver;

    public string StepName => "StartApiContainer";

    public StartApiContainerStep(HubDbContext dbContext, IDockerService dockerService, IConfiguration configuration, TopologyResolver resolver)
    {
        _dbContext = dbContext;
        _dockerService = dockerService;
        _hubConnectionString = configuration.GetSection("Database:ConnectionString").Value
            ?? throw new InvalidOperationException("Database:ConnectionString not configured");
        _resolver = resolver;
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
            // Deserialize tier data from InstanceConfig (set by EnforceTierLimitsStep)
            FeatureFlags? featureFlags = null;
            ResourceLimits? limits = null;
            ContainerResourceLimits? containerResourceLimits = null;

            if (instance.Config?.FeatureFlagsJson != null)
            {
                featureFlags = JsonSerializer.Deserialize<FeatureFlags>(instance.Config.FeatureFlagsJson);
            }

            if (instance.Config?.ResourceLimitsJson != null)
            {
                limits = JsonSerializer.Deserialize<ResourceLimits>(instance.Config.ResourceLimitsJson);
                if (limits != null)
                {
                    containerResourceLimits = new ContainerResourceLimits(
                        MemoryBytes: (long)limits.MaxMemoryMb * 1024 * 1024,
                        CpuQuota: (long)limits.MaxCpuPercent * 1000
                    );
                }
            }

            // Resolve pool-specific service endpoints, falling back to defaults.
            // Data pool overrides compute pool for DB, Redis, and storage when configured.
            var pool = instance.Infrastructure.PlacedInPool;
            var dataPool = instance.Infrastructure.PlacedInDataPool;
            var poolDbConnStr = _resolver.GetDatabaseConnectionString(pool, dataPool);
            var dbConnStr = poolDbConnStr ?? _hubConnectionString;
            var redisConnStr = _resolver.GetRedisConnectionString(pool, dataPool) ?? "redis:6379";
            var storageConfig = _resolver.GetStorageConfig(pool, dataPool);
            var storageEndpoint = storageConfig != null ? $"http://{storageConfig.Endpoint}" : "http://minio:9000";
            var livekitConfig = _resolver.GetLiveKitConfig(pool);
            var livekitHost = livekitConfig?.Host ?? "ws://livekit:7880";

            // Generate config JSON in xcord-config.json format (read by xcord-fed entrypoint).
            // Use per-instance MinIO credentials provisioned by ProvisionMinioStep.
            var configJson = GenerateConfigJson(
                instance.Domain, instance.Infrastructure, instance.SnowflakeWorkerId,
                dbConnStr, instance.Infrastructure.MinioAccessKey, instance.Infrastructure.MinioSecretKey,
                redisConnStr, storageEndpoint, livekitHost,
                featureFlags, limits);

            // Create a Docker secret containing the config. The secret is mounted at
            // /run/secrets/xcord-config inside the container and read by entrypoint.sh.
            // This keeps sensitive credentials out of `docker inspect` and /proc/<pid>/environ.
            var secretId = await _dockerService.CreateSecretAsync(instance.Domain, configJson, cancellationToken);

            // Start container with config secret + KEK secret (separate Docker secrets).
            // The KEK secret was created in GenerateSecretsStep and is reused across
            // container recreations — only deleted during full instance destruction.
            var kekSecretId = !string.IsNullOrWhiteSpace(instance.Infrastructure.DockerKekSecretId)
                ? instance.Infrastructure.DockerKekSecretId
                : null;
            var containerId = await _dockerService.StartContainerAsync(
                instance.Domain, secretId, kekSecretId, containerResourceLimits, cancellationToken);

            // Track the deployed image (pool-specific or fallback)
            instance.Infrastructure.DeployedImage = _resolver.GetInstanceImageForPool(pool);

            // Update infrastructure with container ID and secret ID
            instance.Infrastructure.DockerContainerId = containerId;
            instance.Infrastructure.DockerSecretId = secretId;
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
    /// xcord-fed/docker/entrypoint.sh reads from /run/secrets/xcord-config.
    /// </summary>
    private static string GenerateConfigJson(
        string domain,
        Entities.InstanceInfrastructure infrastructure,
        long workerId,
        string hubConnectionString,
        string minioAccessKey,
        string minioSecretKey,
        string redisConnectionString,
        string storageEndpoint,
        string livekitHost,
        FeatureFlags? featureFlags = null,
        ResourceLimits? resourceLimits = null)
    {
        // Domain format for the instance: subdomain is used as-is (no suffix appended here —
        // the full domain (e.g. "test.localhost") is stored in ManagedInstance.Domain).
        var subdomain = ValidationHelpers.ExtractSubdomain(domain);

        // Build instance DB connection using per-instance credentials when available,
        // falling back to hub superuser for backward compatibility.
        var hubBuilder = new Npgsql.NpgsqlConnectionStringBuilder(hubConnectionString);
        var instanceConnStr = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = hubBuilder.Host,
            Port = hubBuilder.Port,
            Database = infrastructure.DatabaseName,
            Username = !string.IsNullOrWhiteSpace(infrastructure.DatabaseUsername)
                ? infrastructure.DatabaseUsername
                : hubBuilder.Username,
            Password = !string.IsNullOrWhiteSpace(infrastructure.DatabaseUsername)
                ? infrastructure.DatabasePassword
                : hubBuilder.Password
        }.ConnectionString;

        var config = new
        {
            database = new
            {
                connectionString = instanceConnStr
            },
            redis = new
            {
                connectionString = redisConnectionString,
                channelPrefix = $"{domain}:"
            },
            jwt = new
            {
                issuer = $"https://{domain}",
                audience = $"https://{domain}"
            },
            storage = new
            {
                endpoint = storageEndpoint,
                accessKey = minioAccessKey,
                secretKey = minioSecretKey,
                bucket = $"xcord-{subdomain}",
                useSsl = false
            },
            livekit = new
            {
                host = livekitHost,
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
                windowSeconds = 60,
                authRegisterPermitLimit = 100,
                authForgotPasswordPermitLimit = 3
            },
            auth = new
            {
                bcryptWorkFactor = 10
            },
            gif = new
            {
                provider = "none",
                apiKey = ""
            },
            outbox = new
            {
                pollingIntervalMs = 500,
                batchSize = 100,
                cleanupIntervalMinutes = 60,
                retentionHours = 24
            },
            tier = new
            {
                canUseVoiceChannels = featureFlags?.CanUseVoiceChannels ?? true,
                canUseVideoChannels = featureFlags?.CanUseVideoChannels ?? true,
                canCreateBots = featureFlags?.CanCreateBots ?? true,
                canUseWebhooks = featureFlags?.CanUseWebhooks ?? true,
                canUseCustomEmoji = featureFlags?.CanUseCustomEmoji ?? true,
                canUseThreads = featureFlags?.CanUseThreads ?? true,
                canUseForumChannels = featureFlags?.CanUseForumChannels ?? true,
                canUseScheduledEvents = featureFlags?.CanUseScheduledEvents ?? true,
                canUseHdVideo = featureFlags?.CanUseHdVideo ?? false,
                canUseSimulcast = featureFlags?.CanUseSimulcast ?? false,
                canUseRecording = featureFlags?.CanUseRecording ?? false,
                maxUsers = resourceLimits?.MaxUsers ?? 0,
                maxServers = resourceLimits?.MaxServers ?? 0,
                maxStorageMb = resourceLimits?.MaxStorageMb ?? 0,
                maxRateLimit = resourceLimits?.MaxRateLimit ?? 0,
                maxVoiceConcurrency = resourceLimits?.MaxVoiceConcurrency ?? 0,
                maxVideoConcurrency = resourceLimits?.MaxVideoConcurrency ?? 0,
                // Quality limits derived from feature tier
                maxAudioBitrateKbps = (featureFlags?.CanUseVoiceChannels ?? true) ? 50 : 0,
                maxVideoBitrateKbps = (featureFlags?.CanUseVideoChannels ?? false)
                    ? ((featureFlags?.CanUseHdVideo ?? false) ? 4000 : 1500) : 0,
                maxVideoWidth = (featureFlags?.CanUseVideoChannels ?? false)
                    ? ((featureFlags?.CanUseHdVideo ?? false) ? 1920 : 1280) : 0,
                maxVideoHeight = (featureFlags?.CanUseVideoChannels ?? false)
                    ? ((featureFlags?.CanUseHdVideo ?? false) ? 1080 : 720) : 0,
                maxVideoFps = (featureFlags?.CanUseVideoChannels ?? false)
                    ? ((featureFlags?.CanUseHdVideo ?? false) ? 60 : 30) : 0,
                maxScreenShareBitrateKbps = (featureFlags?.CanUseVideoChannels ?? false)
                    ? ((featureFlags?.CanUseHdVideo ?? false) ? 3000 : 1000) : 0
            }
        };

        return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = false });
    }
}
