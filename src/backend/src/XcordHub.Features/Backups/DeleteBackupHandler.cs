using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using XcordHub;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Backups;

public sealed record DeleteBackupCommand(long InstanceId, long BackupId);

public sealed class DeleteBackupHandler(
    HubDbContext dbContext,
    IColdStorageService coldStorageService,
    ILogger<DeleteBackupHandler> logger)
    : IRequestHandler<DeleteBackupCommand, Result<SuccessResponse>>
{
    public async Task<Result<SuccessResponse>> Handle(DeleteBackupCommand request, CancellationToken cancellationToken)
    {
        var instanceExists = await dbContext.ManagedInstances
            .AnyAsync(i => i.Id == request.InstanceId && i.DeletedAt == null, cancellationToken);

        if (!instanceExists)
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");

        var backup = await dbContext.BackupRecords
            .FirstOrDefaultAsync(r => r.Id == request.BackupId && r.ManagedInstanceId == request.InstanceId, cancellationToken);

        if (backup is null)
            return Error.NotFound("BACKUP_NOT_FOUND", "Backup record not found");

        // Soft-delete the record
        backup.DeletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        // Delete storage objects - best effort; log but don't fail if storage deletion fails
        if (!string.IsNullOrEmpty(backup.StoragePath))
        {
            try
            {
                var objects = await coldStorageService.ListObjectsAsync(backup.StoragePath, cancellationToken);
                foreach (var key in objects)
                {
                    await coldStorageService.DeleteAsync(key, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to delete S3 objects for backup {BackupId} at path {StoragePath}",
                    backup.Id, backup.StoragePath);
            }
        }

        return new SuccessResponse(true);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapDelete("/api/v1/admin/instances/{id:long}/backups/{backupId:long}", async (
            long id,
            long backupId,
            DeleteBackupHandler handler,
            CancellationToken ct) =>
        {
            var command = new DeleteBackupCommand(id, backupId);
            return await handler.ExecuteAsync(command, ct);
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<SuccessResponse>(200)
        .WithName("AdminDeleteBackup")
        .WithTags("Admin");
    }
}
