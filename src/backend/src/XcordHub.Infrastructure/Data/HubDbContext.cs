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
    public DbSet<MailingListEntry> MailingListEntries => Set<MailingListEntry>();
    public DbSet<ContactSubmission> ContactSubmissions => Set<ContactSubmission>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();

    // Upgrade management
    public DbSet<AvailableVersion> AvailableVersions => Set<AvailableVersion>();
    public DbSet<UpgradeRollout> UpgradeRollouts => Set<UpgradeRollout>();
    public DbSet<UpgradeEvent> UpgradeEvents => Set<UpgradeEvent>();

    // Revenue tracking
    public DbSet<InstanceRevenueConfig> InstanceRevenueConfigs => Set<InstanceRevenueConfig>();
    public DbSet<PlatformRevenue> PlatformRevenues => Set<PlatformRevenue>();

    // Backup management
    public DbSet<BackupPolicy> BackupPolicies => Set<BackupPolicy>();
    public DbSet<BackupRecord> BackupRecords => Set<BackupRecord>();

    // Uptime tracking
    public DbSet<UptimeInterval> UptimeIntervals => Set<UptimeInterval>();

    // Server lists (header bar)
    public DbSet<ServerList> ServerLists => Set<ServerList>();
    public DbSet<ServerListEntry> ServerListEntries => Set<ServerListEntry>();

    // System-wide admin config (singleton)
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HubDbContext).Assembly);

        new InstanceInfrastructureConfiguration(_encryptionService).Configure(modelBuilder.Entity<InstanceInfrastructure>());
    }
}
