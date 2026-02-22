namespace XcordHub.Entities;

public sealed class HubUser
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public byte[] Email { get; set; } = Array.Empty<byte>();
    public byte[] EmailHash { get; set; } = Array.Empty<byte>();
    public string PasswordHash { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsDisabled { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public string? TwoFactorSecret { get; set; }

    /// <summary>The user's account-level subscription plan governing instance creation quotas and tier defaults.</summary>
    public BillingTier SubscriptionTier { get; set; } = BillingTier.Free;

    /// <summary>Stripe customer ID associated with this user's subscription, if any.</summary>
    public string? StripeCustomerId { get; set; }

    /// <summary>Stripe subscription ID for the user's account-level plan, if any.</summary>
    public string? StripeSubscriptionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigation properties
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
    public ICollection<ManagedInstance> ManagedInstances { get; set; } = new List<ManagedInstance>();
}
