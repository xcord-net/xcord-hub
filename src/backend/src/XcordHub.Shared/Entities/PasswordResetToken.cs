namespace XcordHub.Entities;

public sealed class PasswordResetToken
{
    public long Id { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public long HubUserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsUsed { get; set; }

    // Navigation properties
    public HubUser HubUser { get; set; } = null!;
}
