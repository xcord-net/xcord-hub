using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using XcordHub.Api.Options;
using XcordHub.Entities;
using XcordHub.Features.Destruction;
using XcordHub.Features.Instances;
using XcordHub.Features.Provisioning;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Api;

public static partial class ServiceCollectionExtensions
{
    private static void AddProvisioning(IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<IProvisioningQueue, DatabaseProvisioningQueue>();

        // Always use real Docker and Caddy - no noop services in any environment.
        // Tests that need to override can replace via DI in their test fixtures.
        services.AddSingleton<IDockerService, HttpDockerService>();
        services.AddSingleton<ICaddyProxyManager, CaddyProxyManager>();

        var dnsProvider = config.GetValue<string>("Dns:Provider", "noop");
        switch (dnsProvider.ToLowerInvariant())
        {
            case "cloudflare":
                services.AddSingleton<IDnsProvider, CloudflareDnsProvider>();
                break;
            case "linode":
                services.AddSingleton<IDnsProvider, LinodeDnsProvider>();
                break;
            case "route53":
                services.AddSingleton<IDnsProvider, Route53DnsProvider>();
                break;
            default:
                services.AddSingleton<IDnsProvider, NoopDnsProvider>();
                break;
        }

        services.AddSingleton<TopologyResolver>();

        // Provisioning pipeline steps
        services.AddScoped<IProvisioningStep, ValidateSubdomainStep>();
        services.AddScoped<IProvisioningStep, EnforceTierLimitsStep>();
        services.AddScoped<IProvisioningStep, GenerateSecretsStep>();
        services.AddScoped<IProvisioningStep, ResolvePlacementStep>();
        services.AddScoped<IProvisioningStep, AllocateWorkerIdStep>();
        services.AddScoped<IProvisioningStep, CreateNetworkStep>();
        services.AddScoped<IProvisioningStep, ProvisionDatabaseStep>();
        services.AddScoped<IProvisioningStep, ProvisionRedisAclStep>();
        services.AddScoped<IProvisioningStep, ProvisionMinioStep>();
        services.AddScoped<IProvisioningStep, StartApiContainerStep>();
        services.AddScoped<IProvisioningStep, ConfigureDnsAndProxyStep>();
        services.AddScoped<IProvisioningStep, ConfigureBackupPolicyStep>();
        services.AddScoped<IProvisioningStep, CreateSubscriptionStep>();

        // Provisioning pipeline
        services.AddScoped<ProvisioningPipeline>();

        // Destruction pipeline steps (reverse order of provisioning)
        services.AddScoped<IDestructionStep, StopContainerStep>();
        services.AddScoped<IDestructionStep, RemoveProxyRouteStep>();
        services.AddScoped<IDestructionStep, RemoveDnsRecordStep>();
        services.AddScoped<IDestructionStep, RemoveContainerStep>();
        services.AddScoped<IDestructionStep, RemoveSecretStep>();
        services.AddScoped<IDestructionStep, RemoveNetworkStep>();
        services.AddScoped<IDestructionStep, RemoveMinioBucketStep>();
        services.AddScoped<IDestructionStep, DropDatabaseStep>();
        services.AddScoped<IDestructionStep, ReleaseRedisSlotStep>();
        services.AddScoped<IDestructionStep, RemoveRedisAclStep>();

        // Destruction pipeline
        services.AddScoped<DestructionPipeline>();
    }

    public static async Task SeedAdminAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var encryptionService = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
        var snowflakeGenerator = scope.ServiceProvider.GetRequiredService<SnowflakeIdGenerator>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var authOptions = scope.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value;

        var adminUsername = configuration.GetSection("Admin:Username").Value;
        var adminEmail = configuration.GetSection("Admin:Email").Value;
        var adminPassword = configuration.GetSection("Admin:Password").Value;

        if (string.IsNullOrWhiteSpace(adminUsername) ||
            string.IsNullOrWhiteSpace(adminEmail) ||
            string.IsNullOrWhiteSpace(adminPassword))
        {
            return; // No admin config, skip seeding
        }

        // Check if admin user already exists
        var emailHash = encryptionService.ComputeHmac(adminEmail.ToLowerInvariant());
        var existingAdmin = await dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.EmailHash == emailHash);

        if (existingAdmin != null)
        {
            return; // Admin already exists
        }

        // Create admin user - offloaded to thread pool to avoid starvation
        var passwordHash = await Task.Run(() => BCrypt.Net.BCrypt.HashPassword(adminPassword, authOptions.BcryptWorkFactor));
        var encryptedEmail = encryptionService.Encrypt(adminEmail.ToLowerInvariant());
        var now = DateTimeOffset.UtcNow;

        var adminUser = new HubUser
        {
            Id = snowflakeGenerator.NextId(),
            Username = adminUsername,
            DisplayName = adminUsername,
            Email = encryptedEmail,
            EmailHash = emailHash,
            PasswordHash = passwordHash,
            IsAdmin = true,
            IsDisabled = false,
            CreatedAt = now,
            LastLoginAt = now
        };

        dbContext.HubUsers.Add(adminUser);
        await dbContext.SaveChangesAsync();

        Log.Information("Admin user '{Username}' created successfully", adminUsername);
    }
}
