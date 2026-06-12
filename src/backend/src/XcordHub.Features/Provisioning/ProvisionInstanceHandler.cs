using System.Text.Json;
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
    InstanceTier Tier = InstanceTier.Free,
    bool MediaEnabled = false,
    string? PaymentMethodId = null,
    bool BillingExempt = false
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
    SnowflakeIdGenerator snowflakeGenerator,
    ICurrentUserService currentUserService,
    IOptions<AuthOptions> authOptions,
    IOptions<StripeOptions> stripeOptions)
    : IRequestHandler<ProvisionInstanceCommand, Result<ProvisionInstanceResponse>>, IValidatable<ProvisionInstanceCommand>
{
    private readonly AuthOptions _authOptions = authOptions.Value;
    private readonly StripeOptions _stripeOptions = stripeOptions.Value;

    public Error? Validate(ProvisionInstanceCommand request)
    {
        // OwnerId == 0 is allowed - means "use the calling user's ID"

        var domainError = ValidationHelpers.ValidateDomain(request.Domain);
        if (domainError != null)
            return domainError;

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return Error.Validation("VALIDATION_FAILED", "Display name is required");

        if (request.DisplayName.Length < 1 || request.DisplayName.Length > 100)
            return Error.Validation("VALIDATION_FAILED", "Display name must be between 1 and 100 characters");

        if (string.IsNullOrWhiteSpace(request.AdminPassword))
            return Error.Validation("VALIDATION_FAILED", "Admin password is required");

        if (request.AdminPassword.Length < 8)
            return Error.Validation("VALIDATION_FAILED", "Admin password must be at least 8 characters");

        if (!Enum.IsDefined(request.Tier))
            return Error.Validation("VALIDATION_FAILED", "Invalid tier");

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

        // Fail fast: a paid instance without a payment method would otherwise be
        // provisioned with no Stripe subscription and nothing ever charging for it.
        // BillingExempt is the deliberate admin escape hatch (internal/test instances).
        var priceCents = TierDefaults.GetTotalPriceCents(request.Tier, request.MediaEnabled);
        if (priceCents > 0 && _stripeOptions.IsConfigured && !request.BillingExempt
            && string.IsNullOrWhiteSpace(request.PaymentMethodId))
        {
            return Error.BadRequest("PAYMENT_METHOD_REQUIRED",
                "A payment method is required for paid tiers (or set billingExempt for non-billed instances)");
        }

        // One free instance per user (permanent limit) - applies to target owner, not admin
        if (request.Tier == InstanceTier.Free)
        {
            var hasFreeInstance = await dbContext.ManagedInstances
                .AnyAsync(i => i.OwnerId == ownerId && i.Billing != null && i.Billing.Tier == InstanceTier.Free, cancellationToken);

            if (hasFreeInstance)
                return Error.BadRequest("FREE_INSTANCE_LIMIT", "This user already has a free instance.");
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
        // Paid + billed instances start AwaitingPayment until CreateSubscriptionStep
        // (or a later checkout) establishes the Stripe subscription; the
        // BillingEnforcer suspends instances left in that state past the grace period.
        var initialBillingStatus = priceCents > 0 && _stripeOptions.IsConfigured && !request.BillingExempt
            ? BillingStatus.AwaitingPayment
            : BillingStatus.Active;

        var billing = new InstanceBilling
        {
            Id = snowflakeGenerator.NextId(),
            ManagedInstanceId = instanceId,
            Tier = request.Tier,
            MediaEnabled = request.MediaEnabled,
            BillingStatus = initialBillingStatus,
            BillingStatusChangedAt = now,
            BillingExempt = request.BillingExempt,
            NextBillingDate = now.AddMonths(1),
            CreatedAt = now
        };

        dbContext.InstanceBillings.Add(billing);

        // Get tier defaults
        var resourceLimits = TierDefaults.GetResourceLimits(request.Tier);
        var featureFlags = TierDefaults.GetFeatureFlags(request.Tier, request.MediaEnabled);

        // Create config record with admin password (BCrypt hashed) - offloaded to thread pool to avoid starvation
        var adminPasswordHash = await Task.Run(() => BCrypt.Net.BCrypt.HashPassword(request.AdminPassword, _authOptions.BcryptWorkFactor)).ConfigureAwait(false);
        var config = new InstanceConfig
        {
            Id = snowflakeGenerator.NextId(),
            ManagedInstanceId = instanceId,
            ConfigJson = JsonSerializer.Serialize(new
            {
                AdminPasswordHash = adminPasswordHash,
                PaymentMethodId = request.PaymentMethodId
            }),
            ResourceLimitsJson = JsonSerializer.Serialize(resourceLimits),
            FeatureFlagsJson = JsonSerializer.Serialize(featureFlags),
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.InstanceConfigs.Add(config);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Enqueue for background processing
        await provisioningQueue.EnqueueAsync(instanceId, cancellationToken).ConfigureAwait(false);

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
