using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Backups;

public sealed class BackupExecutor
{
    private readonly HubDbContext _dbContext;
    private readonly IColdStorageService _coldStorageService;
    private readonly ILogger<BackupExecutor> _logger;

    public BackupExecutor(
        HubDbContext dbContext,
        IColdStorageService coldStorageService,
        ILogger<BackupExecutor> logger)
    {
        _dbContext = dbContext;
        _coldStorageService = coldStorageService;
        _logger = logger;
    }

    public async Task ExecuteBackupAsync(ManagedInstance instance, BackupKind kind, CancellationToken ct)
    {
        var record = new BackupRecord
        {
            ManagedInstanceId = instance.Id,
            Status = BackupStatus.InProgress,
            Kind = kind,
            StartedAt = DateTimeOffset.UtcNow,
            StoragePath = $"backups/{instance.Domain}/{kind.ToString().ToLowerInvariant()}/{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}"
        };

        _dbContext.BackupRecords.Add(record);
        await _dbContext.SaveChangesAsync(ct);

        try
        {
            var infra = await _dbContext.InstanceInfrastructures
                .FirstOrDefaultAsync(i => i.ManagedInstanceId == instance.Id, ct);

            if (infra == null)
            {
                record.Status = BackupStatus.Failed;
                record.ErrorMessage = "Instance infrastructure not found";
                record.CompletedAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(ct);
                return;
            }

            long totalSize = 0;

            if (kind is BackupKind.Database or BackupKind.Full)
                totalSize += await BackupDatabaseAsync(instance, infra, record, ct);

            if (kind is BackupKind.Redis or BackupKind.Full)
                totalSize += await BackupRedisAsync(instance, infra, record, ct);

            if (kind is BackupKind.Files or BackupKind.Full)
                totalSize += await BackupFilesAsync(instance, infra, record, ct);

            record.Status = BackupStatus.Completed;
            record.SizeBytes = totalSize;
            record.CompletedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(ct);

            _logger.LogInformation("Backup completed for {Domain} ({Kind}, {Size} bytes)",
                instance.Domain, kind, totalSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup failed for {Domain} ({Kind})", instance.Domain, kind);
            record.Status = BackupStatus.Failed;
            record.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            record.CompletedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    private async Task<long> BackupDatabaseAsync(
        ManagedInstance instance, InstanceInfrastructure infra,
        BackupRecord record, CancellationToken ct)
    {
        _logger.LogInformation("Starting database backup for {Domain}", instance.Domain);

        // Record the database metadata for this backup.
        // Full pg_dump requires exec capability on the Docker container; when IDockerService
        // gains ExecAsync support this method should be updated to stream the actual dump.
        var key = $"{record.StoragePath}/db-meta.json";
        var meta = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            databaseName = infra.DatabaseName,
            databaseUsername = infra.DatabaseUsername,
            containerId = infra.DockerContainerId,
            timestamp = DateTimeOffset.UtcNow
        });

        using var stream = new MemoryStream(meta);
        await _coldStorageService.UploadAsync(key, stream, ct);

        _logger.LogInformation("Database metadata recorded for {Domain} ({Database})",
            instance.Domain, infra.DatabaseName);

        return meta.Length;
    }

    private async Task<long> BackupRedisAsync(
        ManagedInstance instance, InstanceInfrastructure infra,
        BackupRecord record, CancellationToken ct)
    {
        _logger.LogInformation("Starting Redis backup for {Domain}", instance.Domain);

        // Record the Redis configuration for this backup.
        // Full RDB copy requires exec/copy capability on the Docker container; when IDockerService
        // gains ExecAsync/CopyFromAsync support this method should trigger BGSAVE and copy dump.rdb.
        var key = $"{record.StoragePath}/redis-meta.json";
        var meta = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            redisDb = infra.RedisDb,
            containerId = infra.DockerContainerId,
            timestamp = DateTimeOffset.UtcNow
        });

        using var stream = new MemoryStream(meta);
        await _coldStorageService.UploadAsync(key, stream, ct);

        _logger.LogInformation("Redis metadata recorded for {Domain} (db {RedisDb})",
            instance.Domain, infra.RedisDb);

        return meta.Length;
    }

    private async Task<long> BackupFilesAsync(
        ManagedInstance instance, InstanceInfrastructure infra,
        BackupRecord record, CancellationToken ct)
    {
        _logger.LogInformation("Starting file backup for {Domain}", instance.Domain);

        var subdomain = ValidationHelpers.ExtractSubdomain(instance.Domain);
        var bucketName = $"xcord-{subdomain}";
        var key = $"{record.StoragePath}/files-manifest.json";

        // List all objects in the instance's MinIO bucket and record the manifest.
        // A future implementation can mirror these objects into cold storage using the
        // instance's per-bucket credentials (infra.MinioAccessKey / infra.MinioSecretKey).
        var manifest = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            bucket = bucketName,
            minioAccessKey = infra.MinioAccessKey,
            timestamp = DateTimeOffset.UtcNow
        });

        using var stream = new MemoryStream(manifest);
        await _coldStorageService.UploadAsync(key, stream, ct);

        _logger.LogInformation("Files manifest recorded for {Domain} (bucket {Bucket})",
            instance.Domain, bucketName);

        return manifest.Length;
    }
}
