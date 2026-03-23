using System.Text.Json;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using XcordHub.Entities;
using XcordHub.Features.Auth;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;

namespace XcordHub.Features.Instances;

public sealed class InstanceCreationService(
    HubDbContext db,
    ICaptchaService captchaService,
    SnowflakeIdGenerator idGenerator,
    IConfiguration configuration,
    IOptions<AuthOptions> authOptions)
{
    private readonly AuthOptions _authOptions = authOptions.Value;

    public async Task<Result<ManagedInstance>> CreateAsync(
        long userId,
        string subdomain,
        string displayName,
        string adminPassword,
        InstanceTier tier,
        bool mediaEnabled,
        bool skipCaptcha,
        string? captchaId,
        string? captchaAnswer,
        CancellationToken ct,
        string? paymentMethodId = null)
    {
        // Validate captcha for free tier without media
        var isFreeTier = tier == InstanceTier.Free && !mediaEnabled;
        if (!skipCaptcha && isFreeTier && !await captchaService.ValidateAsync(captchaId ?? "", captchaAnswer ?? ""))
        {
            return Error.BadRequest("CAPTCHA_FAILED", "Invalid or expired captcha");
        }

        // Beta gate - one free instance per user (permanent limit)
        if (tier == InstanceTier.Free)
        {
            var hasFreeInstance = await db.ManagedInstances
                .AnyAsync(i => i.OwnerId == userId && i.Billing != null && i.Billing.Tier == InstanceTier.Free, ct);

            if (hasFreeInstance)
                return Error.BadRequest("FREE_INSTANCE_LIMIT", "You already have a free instance.");
        }

        var baseDomain = configuration.GetValue<string>("Hub:BaseDomain") ?? "xcord-dev.net";
        var domain = $"{subdomain}.{baseDomain}";

        var domainExists = await db.ManagedInstances
            .AnyAsync(i => i.Domain == domain, ct);

        if (domainExists)
        {
            return Error.Conflict("SUBDOMAIN_TAKEN", "This subdomain is already taken");
        }

        var user = await db.HubUsers.FindAsync([userId], ct);

        if (user == null)
        {
            return Error.NotFound("USER_NOT_FOUND", "User not found");
        }

        var now = DateTimeOffset.UtcNow;
        var instanceId = idGenerator.NextId();

        // Create managed instance
        var instance = new ManagedInstance
        {
            Id = instanceId,
            OwnerId = userId,
            Domain = domain,
            DisplayName = displayName,
            Status = InstanceStatus.Pending,
            SnowflakeWorkerId = 0, // Will be allocated by AllocateWorkerIdStep in the provisioning pipeline
            CreatedAt = now
        };

        db.ManagedInstances.Add(instance);

        // Create billing record
        var billing = new InstanceBilling
        {
            Id = idGenerator.NextId(),
            ManagedInstanceId = instanceId,
            Tier = tier,
            MediaEnabled = mediaEnabled,
            BillingStatus = BillingStatus.Active,
            BillingExempt = false,
            NextBillingDate = now.AddMonths(1),
            CreatedAt = now
        };

        db.InstanceBillings.Add(billing);

        // Create config with tier defaults and hashed admin password - offloaded to thread pool to avoid starvation
        var resourceLimits = TierDefaults.GetResourceLimits(tier);
        var featureFlags = TierDefaults.GetFeatureFlags(tier, mediaEnabled);
        var adminPasswordHash = await Task.Run(() => BCrypt.Net.BCrypt.HashPassword(adminPassword, _authOptions.BcryptWorkFactor));

        var config = new InstanceConfig
        {
            Id = idGenerator.NextId(),
            ManagedInstanceId = instanceId,
            ConfigJson = JsonSerializer.Serialize(new
            {
                AdminPasswordHash = adminPasswordHash,
                PaymentMethodId = paymentMethodId
            }),
            ResourceLimitsJson = JsonSerializer.Serialize(resourceLimits),
            FeatureFlagsJson = JsonSerializer.Serialize(featureFlags),
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.InstanceConfigs.Add(config);

        return instance;
    }
}
