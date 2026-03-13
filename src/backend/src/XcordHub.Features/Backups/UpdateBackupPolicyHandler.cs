using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;

namespace XcordHub.Features.Backups;

public sealed record UpdateBackupPolicyCommand(
    long InstanceId,
    bool Enabled,
    string Frequency,
    int RetentionDays,
    bool BackupDatabase,
    bool BackupFiles,
    bool BackupRedis
);

public sealed record UpdateBackupPolicyRequest(
    bool Enabled,
    string Frequency,
    int RetentionDays,
    bool BackupDatabase,
    bool BackupFiles,
    bool BackupRedis
);

public sealed class UpdateBackupPolicyHandler(HubDbContext dbContext)
    : IRequestHandler<UpdateBackupPolicyCommand, Result<BackupPolicyResponse>>
{
    public async Task<Result<BackupPolicyResponse>> Handle(UpdateBackupPolicyCommand request, CancellationToken cancellationToken)
    {
        var instanceExists = await dbContext.ManagedInstances
            .AnyAsync(i => i.Id == request.InstanceId && i.DeletedAt == null, cancellationToken);

        if (!instanceExists)
            return Error.NotFound("INSTANCE_NOT_FOUND", "Instance not found");

        if (!Enum.TryParse<BackupFrequency>(request.Frequency, ignoreCase: true, out var frequency))
            return Error.Validation("INVALID_FREQUENCY", $"Frequency must be one of: {string.Join(", ", Enum.GetNames<BackupFrequency>())}");

        if (request.RetentionDays < 1 || request.RetentionDays > 365)
            return Error.Validation("INVALID_RETENTION", "RetentionDays must be between 1 and 365");

        var now = DateTimeOffset.UtcNow;

        var policy = await dbContext.BackupPolicies
            .FirstOrDefaultAsync(p => p.ManagedInstanceId == request.InstanceId, cancellationToken);

        if (policy is null)
        {
            policy = new BackupPolicy
            {
                ManagedInstanceId = request.InstanceId,
                CreatedAt = now
            };
            dbContext.BackupPolicies.Add(policy);
        }

        policy.Enabled = request.Enabled;
        policy.Frequency = frequency;
        policy.RetentionDays = request.RetentionDays;
        policy.BackupDatabase = request.BackupDatabase;
        policy.BackupFiles = request.BackupFiles;
        policy.BackupRedis = request.BackupRedis;
        policy.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

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
        return app.MapPut("/api/v1/admin/instances/{id:long}/backup-policy", async (
            long id,
            UpdateBackupPolicyRequest body,
            UpdateBackupPolicyHandler handler,
            CancellationToken ct) =>
        {
            var command = new UpdateBackupPolicyCommand(
                id,
                body.Enabled,
                body.Frequency,
                body.RetentionDays,
                body.BackupDatabase,
                body.BackupFiles,
                body.BackupRedis);
            return await handler.ExecuteAsync(command, ct);
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<BackupPolicyResponse>(200)
        .WithName("AdminUpdateBackupPolicy")
        .WithTags("Admin");
    }
}
