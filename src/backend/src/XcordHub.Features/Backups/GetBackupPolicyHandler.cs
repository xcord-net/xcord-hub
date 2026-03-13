using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Backups;

public sealed record GetBackupPolicyQuery(long InstanceId);

public sealed record BackupPolicyResponse(
    string InstanceId,
    bool Enabled,
    string Frequency,
    int RetentionDays,
    bool BackupDatabase,
    bool BackupFiles,
    bool BackupRedis
);

public sealed class GetBackupPolicyHandler(HubDbContext dbContext)
    : IRequestHandler<GetBackupPolicyQuery, Result<BackupPolicyResponse>>
{
    public async Task<Result<BackupPolicyResponse>> Handle(GetBackupPolicyQuery request, CancellationToken cancellationToken)
    {
        var instanceExists = await dbContext.ManagedInstances
            .AnyAsync(i => i.Id == request.InstanceId && i.DeletedAt == null, cancellationToken);

        if (!instanceExists)
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");

        var policy = await dbContext.BackupPolicies
            .FirstOrDefaultAsync(p => p.ManagedInstanceId == request.InstanceId, cancellationToken);

        if (policy is null)
        {
            // Return default policy when none has been configured
            return new BackupPolicyResponse(
                request.InstanceId.ToString(),
                Enabled: true,
                Frequency: BackupFrequency.Daily.ToString(),
                RetentionDays: 30,
                BackupDatabase: true,
                BackupFiles: true,
                BackupRedis: true);
        }

        return new BackupPolicyResponse(
            policy.ManagedInstanceId.ToString(),
            policy.Enabled,
            policy.Frequency.ToString(),
            policy.RetentionDays,
            policy.BackupDatabase,
            policy.BackupFiles,
            policy.BackupRedis);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/admin/instances/{id:long}/backup-policy", async (
            long id,
            GetBackupPolicyHandler handler,
            CancellationToken ct) =>
        {
            var query = new GetBackupPolicyQuery(id);
            return await handler.ExecuteAsync(query, ct);
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<BackupPolicyResponse>(200)
        .WithName("AdminGetBackupPolicy")
        .WithTags("Admin");
    }
}
