namespace XcordHub.Entities;

public sealed class MailingListEntry
{
    public long Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
