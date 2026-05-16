namespace XcordHub.Infrastructure.Options;

public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;

    /// <summary>
    /// Signing secret for the platform-account Stripe webhook
    /// (POST /api/v1/hub/billing/stripe-webhook). Handles platform-level events such as
    /// instance subscription lifecycle, invoice paid/failed, etc.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);
}
