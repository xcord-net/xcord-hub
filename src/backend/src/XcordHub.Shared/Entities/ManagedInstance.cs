using XcordHub.Entities;

namespace XcordHub.Entities;

public sealed class ManagedInstance : ISoftDeletable
{
    public long Id { get; set; }
    public long OwnerId { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public int MemberCount { get; set; }
    public int OnlineCount { get; set; }
    public InstanceStatus Status { get; set; }
    public long SnowflakeWorkerId { get; set; }

    /// <summary>
    /// Number of times provisioning has been attempted (initial enqueue plus
    /// reconciler retries). The reconciler marks the instance Failed once this
    /// reaches its retry cap.
    /// </summary>
    public int ProvisioningAttempts { get; set; }

    /// <summary>
    /// When provisioning was last enqueued. Stuck-detection measures from this
    /// timestamp (not CreatedAt, which never resets across retries).
    /// </summary>
    public DateTimeOffset? LastProvisioningAttemptAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigation properties
    public HubUser Owner { get; set; } = null!;
    public InstanceInfrastructure? Infrastructure { get; set; }
    public InstanceBilling? Billing { get; set; }
    public InstanceConfig? Config { get; set; }
    public InstanceHealth? Health { get; set; }
    public ICollection<ProvisioningEvent> ProvisioningEvents { get; set; } = new List<ProvisioningEvent>();
    public ICollection<FederationToken> FederationTokens { get; set; } = new List<FederationToken>();
    public ICollection<UpgradeEvent> UpgradeEvents { get; set; } = new List<UpgradeEvent>();
    public BackupPolicy? BackupPolicy { get; set; }
    public ICollection<BackupRecord> BackupRecords { get; set; } = new List<BackupRecord>();
    public ICollection<UptimeInterval> UptimeIntervals { get; set; } = new List<UptimeInterval>();
}
