using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Backups;

public sealed record TriggerBackupCommand(long InstanceId, string Kind);

public sealed record TriggerBackupRequest(string Kind);

public sealed class TriggerBackupHandler(HubDbContext dbContext, SnowflakeId snowflakeId)
    : IRequestHandler<TriggerBackupCommand, Result<BackupRecordItem>>
{
    public async Task<Result<BackupRecordItem>> Handle(TriggerBackupCommand request, CancellationToken cancellationToken)
    {
        var instanceExists = await dbContext.ManagedInstances
            .AnyAsync(i => i.Id == request.InstanceId && i.DeletedAt == null, cancellationToken);

        if (!instanceExists)
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");

        if (!Enum.TryParse<BackupKind>(request.Kind, ignoreCase: true, out var kind))
            return Error.Validation("INVALID_KIND", $"Kind must be one of: {string.Join(", ", Enum.GetNames<BackupKind>())}");

        var now = DateTimeOffset.UtcNow;
        var storagePath = $"backups/{request.InstanceId}/{kind.ToString().ToLowerInvariant()}/{now:yyyyMMdd-HHmmss}";

        var record = new BackupRecord
        {
            Id = snowflakeId.NextId(),
            ManagedInstanceId = request.InstanceId,
            Status = BackupStatus.InProgress,
            Kind = kind,
            SizeBytes = 0,
            StoragePath = storagePath,
            StartedAt = now
        };

        dbContext.BackupRecords.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new BackupRecordItem(
            record.Id.ToString(),
            record.ManagedInstanceId.ToString(),
            record.Status.ToString(),
            record.Kind.ToString(),
            record.SizeBytes,
            record.StoragePath,
            record.ErrorMessage,
            record.StartedAt,
            record.CompletedAt);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/admin/instances/{id:long}/backups/trigger", async (
            long id,
            TriggerBackupRequest body,
            TriggerBackupHandler handler,
            CancellationToken ct) =>
        {
            var command = new TriggerBackupCommand(id, body.Kind);
            return await handler.ExecuteAsync(command, ct, result => Results.Created($"/api/v1/admin/instances/{id}/backups", result));
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<BackupRecordItem>(201)
        .WithName("AdminTriggerBackup")
        .WithTags("Admin");
    }
}
