namespace XcordHub.Entities;

public sealed class AvailableVersion
{
    public long Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string? ReleaseNotes { get; set; }
    public bool IsMinimumVersion { get; set; }
    public DateTimeOffset? MinimumEnforcementDate { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public long PublishedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigation properties
    public HubUser Publisher { get; set; } = null!;
}
