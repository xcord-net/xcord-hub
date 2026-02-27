using System.Text.Json;
using System.Text.RegularExpressions;
using BCrypt.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Provisioning;

public sealed record ProvisionInstanceCommand(
    long OwnerId,
    string Domain,
    string DisplayName,
    string AdminPassword,
    FeatureTier FeatureTier = FeatureTier.Chat,
    UserCountTier UserCountTier = UserCountTier.Tier10,
    bool HdUpgrade = false
);

public sealed record ProvisionInstanceResponse(
    string InstanceId,
    string Domain,
    string DisplayName,
    string AdminPassword
);

public sealed class ProvisionInstanceHandler(
    HubDbContext dbContext,
    IProvisioningQueue provisioningQueue,
    SnowflakeId snowflakeGenerator,
    ICurrentUserService currentUserService,
    IOptions<AuthOptions> authOptions)
    : IRequestHandler<ProvisionInstanceCommand, Result<ProvisionInstanceResponse>>, IValidatable<ProvisionInstanceCommand>
{
    private readonly AuthOptions _authOptions = authOptions.Value;

    public Error? Validate(ProvisionInstanceCommand request)
    {
        // OwnerId == 0 is allowed — means "use the calling user's ID"

        if (string.IsNullOrWhiteSpace(request.Domain))
            return Error.Validation("VALIDATION_FAILED", "Domain is required");

        if (!Regex.IsMatch(request.Domain, @"^[a-z0-9]([a-z0-9.-]*[a-z0-9])?$"))
            return Error.Validation("VALIDATION_FAILED", "Domain must contain only lowercase letters, numbers, hyphens, and dots");

        if (request.Domain.Length < 3 || request.Domain.Length > 253)
            return Error.Validation("VALIDATION_FAILED", "Domain must be between 3 and 253 characters");

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return Error.Validation("VALIDATION_FAILED", "Display name is required");

        if (request.DisplayName.Length < 1 || request.DisplayName.Length > 100)
            return Error.Validation("VALIDATION_FAILED", "Display name must be between 1 and 100 characters");

        if (string.IsNullOrWhiteSpace(request.AdminPassword))
            return Error.Validation("VALIDATION_FAILED", "Admin password is required");

        if (request.AdminPassword.Length < 8)
            return Error.Validation("VALIDATION_FAILED", "Admin password must be at least 8 characters");

        if (!Enum.IsDefined(request.FeatureTier))
            return Error.Validation("VALIDATION_FAILED", "Invalid feature tier");

        if (!Enum.IsDefined(request.UserCountTier))
            return Error.Validation("VALIDATION_FAILED", "Invalid user count tier");

        if (request.HdUpgrade && request.FeatureTier != FeatureTier.Video)
            return Error.Validation("VALIDATION_FAILED", "HD upgrade requires Video feature tier");

        return null;
    }

    public async Task<Result<ProvisionInstanceResponse>> Handle(ProvisionInstanceCommand request, CancellationToken cancellationToken)
    {
        // Resolve owner ID: if not supplied (0), default to the calling user
        var ownerId = request.OwnerId;
        if (ownerId <= 0)
        {
            var userIdResult = currentUserService.GetCurrentUserId();
            if (userIdResult.IsFailure) return userIdResult.Error!;
            ownerId = userIdResult.Value;
        }

        // Verify owner exists
        var ownerExists = await dbContext.HubUsers
            .AnyAsync(u => u.Id == ownerId && u.DeletedAt == null, cancellationToken);

        if (!ownerExists)
        {
            return Error.NotFound("OWNER_NOT_FOUND", $"Owner {ownerId} not found");
        }

        // Check if domain already exists (excluding soft-deleted instances)
        var domainExists = await dbContext.ManagedInstances
            .IgnoreQueryFilters()
            .AnyAsync(i => i.Domain == request.Domain && i.DeletedAt == null, cancellationToken);

        if (domainExists)
        {
            return Error.Conflict("DOMAIN_TAKEN", $"Domain {request.Domain} is already taken");
        }

        // Create instance record with Pending status
        var instanceId = snowflakeGenerator.NextId();
        var now = DateTimeOffset.UtcNow;

        var instance = new ManagedInstance
        {
            Id = instanceId,
            OwnerId = ownerId,
            Domain = request.Domain,
            DisplayName = request.DisplayName,
            Status = InstanceStatus.Pending,
            SnowflakeWorkerId = 0, // Will be allocated in pipeline
            CreatedAt = now
        };

        dbContext.ManagedInstances.Add(instance);

        // Create billing record with requested tier
        var billing = new InstanceBilling
        {
            Id = snowflakeGenerator.NextId(),
            ManagedInstanceId = instanceId,
            FeatureTier = request.FeatureTier,
            UserCountTier = request.UserCountTier,
            HdUpgrade = request.HdUpgrade,
            BillingStatus = BillingStatus.Active,
            BillingExempt = false,
            NextBillingDate = now.AddMonths(1),
            CreatedAt = now
        };

        dbContext.InstanceBillings.Add(billing);

        // Get tier defaults
        var resourceLimits = TierDefaults.GetResourceLimits(request.UserCountTier);
        var featureFlags = TierDefaults.GetFeatureFlags(request.FeatureTier, request.HdUpgrade);

        // Create config record with admin password (BCrypt hashed) — offloaded to thread pool to avoid starvation
        var adminPasswordHash = await Task.Run(() => BCrypt.Net.BCrypt.HashPassword(request.AdminPassword, _authOptions.BcryptWorkFactor));
        var config = new InstanceConfig
        {
            Id = snowflakeGenerator.NextId(),
            ManagedInstanceId = instanceId,
            ConfigJson = JsonSerializer.Serialize(new
            {
                AdminPasswordHash = adminPasswordHash
            }),
            ResourceLimitsJson = JsonSerializer.Serialize(resourceLimits),
            FeatureFlagsJson = JsonSerializer.Serialize(featureFlags),
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.InstanceConfigs.Add(config);

        await dbContext.SaveChangesAsync(cancellationToken);

        // Enqueue for background processing
        await provisioningQueue.EnqueueAsync(instanceId, cancellationToken);

        // Return 201 with instance details and plaintext admin password
        return new ProvisionInstanceResponse(
            instanceId.ToString(),
            request.Domain,
            request.DisplayName,
            request.AdminPassword // Plaintext password returned only in response
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/admin/instances", async (
            ProvisionInstanceCommand request,
            ProvisionInstanceHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(request, ct, success =>
                Results.Created($"/api/v1/admin/instances/{success.InstanceId}", success));
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<ProvisionInstanceResponse>(201)
        .WithName("ProvisionInstance")
        .WithTags("Admin");
    }
}
