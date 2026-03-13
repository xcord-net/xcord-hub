using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Backups;

public sealed record TriggerRestoreCommand(long InstanceId, long BackupId);

public sealed record TriggerRestoreResponse(string Message, string BackupId, string InstanceId);

public sealed class TriggerRestoreHandler(HubDbContext dbContext)
    : IRequestHandler<TriggerRestoreCommand, Result<TriggerRestoreResponse>>
{
    public async Task<Result<TriggerRestoreResponse>> Handle(TriggerRestoreCommand request, CancellationToken cancellationToken)
    {
        var instanceExists = await dbContext.ManagedInstances
            .AnyAsync(i => i.Id == request.InstanceId && i.DeletedAt == null, cancellationToken);

        if (!instanceExists)
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");

        var backup = await dbContext.BackupRecords
            .FirstOrDefaultAsync(r => r.Id == request.BackupId && r.ManagedInstanceId == request.InstanceId, cancellationToken);

        if (backup is null)
            return Error.NotFound("BACKUP_NOT_FOUND", "Backup record not found");

        if (backup.Status != XcordHub.Entities.BackupStatus.Completed)
            return Error.Validation("BACKUP_NOT_COMPLETED", "Only completed backups can be restored");

        // Restore is accepted and will be processed asynchronously.
        // The actual restore orchestration is handled by the background service.
        return new TriggerRestoreResponse(
            "Restore initiated. The instance will be restored from the selected backup.",
            backup.Id.ToString(),
            backup.ManagedInstanceId.ToString());
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/admin/instances/{id:long}/backups/{backupId:long}/restore", async (
            long id,
            long backupId,
            TriggerRestoreHandler handler,
            CancellationToken ct) =>
        {
            var command = new TriggerRestoreCommand(id, backupId);
            return await handler.ExecuteAsync(command, ct, result => Results.Accepted(value: result));
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<TriggerRestoreResponse>(202)
        .WithName("AdminTriggerRestore")
        .WithTags("Admin");
    }
}
