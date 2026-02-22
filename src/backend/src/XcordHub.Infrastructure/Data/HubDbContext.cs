using Microsoft.EntityFrameworkCore;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data.Configurations;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Infrastructure.Data;

public sealed class HubDbContext : DbContext
{
    private readonly IEncryptionService _encryptionService;

    public HubDbContext(DbContextOptions<HubDbContext> options, IEncryptionService encryptionService)
        : base(options)
    {
        _encryptionService = encryptionService;
    }

    public DbSet<ManagedInstance> ManagedInstances => Set<ManagedInstance>();
    public DbSet<InstanceInfrastructure> InstanceInfrastructures => Set<InstanceInfrastructure>();
    public DbSet<InstanceBilling> InstanceBillings => Set<InstanceBilling>();
    public DbSet<InstanceConfig> InstanceConfigs => Set<InstanceConfig>();
    public DbSet<InstanceHealth> InstanceHealths => Set<InstanceHealth>();
    public DbSet<HubUser> HubUsers => Set<HubUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<ProvisioningEvent> ProvisioningEvents => Set<ProvisioningEvent>();
    public DbSet<WorkerIdRegistry> WorkerIdRegistry => Set<WorkerIdRegistry>();
    public DbSet<FederationToken> FederationTokens => Set<FederationToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply configurations that don't require injected services via assembly scan
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(HubDbContext).Assembly,
            t => t != typeof(InstanceInfrastructureConfiguration));

        // Apply configurations that require injected services explicitly
        modelBuilder.ApplyConfiguration(new InstanceInfrastructureConfiguration(_encryptionService));
    }
}
