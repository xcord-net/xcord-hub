using XcordHub.Entities;

namespace XcordHub.Entities;

public sealed class ManagedInstance
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
}
