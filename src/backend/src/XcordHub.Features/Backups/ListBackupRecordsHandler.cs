using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Backups;

public sealed record ListBackupRecordsQuery(long InstanceId, int Page, int PageSize);

public sealed record BackupRecordItem(
    string Id,
    string InstanceId,
    string Status,
    string Kind,
    long SizeBytes,
    string StoragePath,
    string? ErrorMessage,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt
);

public sealed record ListBackupRecordsResponse(
    List<BackupRecordItem> Backups,
    int Total,
    int Page,
    int PageSize
);

public sealed class ListBackupRecordsHandler(HubDbContext dbContext)
    : IRequestHandler<ListBackupRecordsQuery, Result<ListBackupRecordsResponse>>
{
    public async Task<Result<ListBackupRecordsResponse>> Handle(ListBackupRecordsQuery request, CancellationToken cancellationToken)
    {
        var instanceExists = await dbContext.ManagedInstances
            .AnyAsync(i => i.Id == request.InstanceId && i.DeletedAt == null, cancellationToken);

        if (!instanceExists)
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var query = dbContext.BackupRecords
            .Where(r => r.ManagedInstanceId == request.InstanceId);

        var total = await query.CountAsync(cancellationToken);

        var records = await query
            .OrderByDescending(r => r.StartedAt)
            .Skip(skip)
            .Take(pageSize)
            .Select(r => new BackupRecordItem(
                r.Id.ToString(),
                r.ManagedInstanceId.ToString(),
                r.Status.ToString(),
                r.Kind.ToString(),
                r.SizeBytes,
                r.StoragePath,
                r.ErrorMessage,
                r.StartedAt,
                r.CompletedAt))
            .ToListAsync(cancellationToken);

        return new ListBackupRecordsResponse(records, total, page, pageSize);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/admin/instances/{id:long}/backups", async (
            long id,
            int page,
            int pageSize,
            ListBackupRecordsHandler handler,
            CancellationToken ct) =>
        {
            var effectivePage = page > 0 ? page : 1;
            var effectivePageSize = pageSize > 0 ? pageSize : 20;
            var query = new ListBackupRecordsQuery(id, effectivePage, effectivePageSize);
            return await handler.ExecuteAsync(query, ct);
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<ListBackupRecordsResponse>(200)
        .WithName("AdminListBackupRecords")
        .WithTags("Admin");
    }
}
