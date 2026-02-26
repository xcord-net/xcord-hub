namespace XcordHub.Entities;

public sealed class LoginAttempt
{
    public long Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? FailureReason { get; set; }
    public long? UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
