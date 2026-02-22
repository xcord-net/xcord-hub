using System.Text.Json;
using System.Text.RegularExpressions;
using BCrypt.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using XcordHub;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Instances;

public sealed record CreateInstanceCommand(
    string Subdomain,
    string DisplayName,
    string? AdminPassword = null
);

public sealed record CreateInstanceResponse(
    long InstanceId,
    string Domain,
    string DisplayName,
    string Status,
    string AdminPassword
);

public sealed class CreateInstanceHandler(
    HubDbContext dbContext,
    SnowflakeId snowflakeGenerator,
    ICurrentUserService currentUserService,
    IProvisioningQueue provisioningQueue,
    IConfiguration configuration)
    : IRequestHandler<CreateInstanceCommand, Result<CreateInstanceResponse>>,
      IValidatable<CreateInstanceCommand>
{
    public Error? Validate(CreateInstanceCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.Subdomain))
            return Error.Validation("VALIDATION_FAILED", "Subdomain is required");

        if (request.Subdomain.Length < 3 || request.Subdomain.Length > 63)
            return Error.Validation("VALIDATION_FAILED", "Subdomain must be 3-63 characters");

        if (!Regex.IsMatch(request.Subdomain, "^[a-z0-9]([a-z0-9-]*[a-z0-9])?$"))
            return Error.Validation("VALIDATION_FAILED", "Subdomain must be 3-63 characters, lowercase alphanumeric with hyphens (not at start/end)");

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return Error.Validation("VALIDATION_FAILED", "DisplayName is required");

        if (request.DisplayName.Length > 255)
            return Error.Validation("VALIDATION_FAILED", "DisplayName must not exceed 255 characters");

        return null;
    }

    public async Task<Result<CreateInstanceResponse>> Handle(CreateInstanceCommand request, CancellationToken cancellationToken)
    {
        var userIdResult = currentUserService.GetCurrentUserId();
        if (userIdResult.IsFailure) return userIdResult.Error!;
        var userId = userIdResult.Value;

        var baseDomain = configuration.GetValue<string>("Hub:BaseDomain") ?? "xcord-dev.net";
        var domain = $"{request.Subdomain}.{baseDomain}";

        var domainExists = await dbContext.ManagedInstances
            .AnyAsync(i => i.Domain == domain, cancellationToken);

        if (domainExists)
        {
            return Error.Conflict("SUBDOMAIN_TAKEN", "This subdomain is already taken");
        }

        var user = await dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            return Error.NotFound("USER_NOT_FOUND", "User not found");
        }

        // Check instance count against the user's account subscription tier quota
        var currentInstanceCount = await dbContext.ManagedInstances
            .CountAsync(i => i.OwnerId == userId, cancellationToken);

        var userTier = user.SubscriptionTier;
        var maxInstances = TierDefaults.GetMaxInstancesForTier(userTier);

        if (maxInstances != -1 && currentInstanceCount >= maxInstances)
        {
            return Error.Forbidden("INSTANCE_LIMIT_REACHED",
                $"You have reached the maximum number of instances ({maxInstances}) for your tier");
        }

        // Allocate snowflake worker ID
        var maxWorkerId = await dbContext.ManagedInstances
            .MaxAsync(i => (long?)i.SnowflakeWorkerId, cancellationToken) ?? 0;
        var workerId = maxWorkerId + 1;

        var now = DateTimeOffset.UtcNow;
        var instanceId = snowflakeGenerator.NextId();

        // Generate admin password if not provided
        var adminPassword = !string.IsNullOrWhiteSpace(request.AdminPassword)
            ? request.AdminPassword
            : Guid.NewGuid().ToString("N")[..16];

        // Create managed instance
        var instance = new ManagedInstance
        {
            Id = instanceId,
            OwnerId = userId,
            Domain = domain,
            DisplayName = request.DisplayName,
            Status = InstanceStatus.Pending,
            SnowflakeWorkerId = workerId,
            CreatedAt = now
        };

        dbContext.ManagedInstances.Add(instance);

        // Create billing record
        var billing = new InstanceBilling
        {
            Id = snowflakeGenerator.NextId(),
            ManagedInstanceId = instanceId,
            Tier = userTier,
            BillingStatus = BillingStatus.Active,
            BillingExempt = false,
            NextBillingDate = now.AddMonths(1),
            CreatedAt = now
        };

        dbContext.InstanceBillings.Add(billing);

        // Create config with tier defaults and hashed admin password
        var resourceLimits = TierDefaults.GetResourceLimits(userTier);
        var featureFlags = TierDefaults.GetFeatureFlags(userTier);
        var adminPasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword, 12);

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

        // Enqueue for background provisioning
        await provisioningQueue.EnqueueAsync(instanceId, cancellationToken);

        return new CreateInstanceResponse(
            instanceId,
            domain,
            request.DisplayName,
            InstanceStatus.Pending.ToString(),
            adminPassword
        );
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/hub/instances", async (
            CreateInstanceCommand command,
            CreateInstanceHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(command, ct, success =>
                Results.Created($"/api/v1/hub/instances/{success.InstanceId}", success));
        })
        .RequireAuthorization(Policies.User)
        .WithName("CreateInstance")
        .WithTags("Instances");
    }
}
