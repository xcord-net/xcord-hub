namespace XcordHub.Entities;

public sealed class FederationToken
{
    public long Id { get; set; }
    public long ManagedInstanceId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    // Navigation properties
    public ManagedInstance ManagedInstance { get; set; } = null!;
}
