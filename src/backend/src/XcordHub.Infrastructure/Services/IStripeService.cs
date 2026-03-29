namespace XcordHub.Infrastructure.Services;

public sealed record CreateCheckoutRequest(
    string CustomerId,
    string PriceId,
    long InstanceId,
    string SuccessUrl,
    string CancelUrl
);

public sealed record CheckoutResult(string SessionId, string CheckoutUrl);

public sealed record StripeInvoice(
    string Id,
    string? Description,
    long AmountCents,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt,
    string? PdfUrl
);

public sealed record SetupIntentResult(string SetupIntentId, string ClientSecret);

public sealed record CreateSubscriptionResult(string SubscriptionId, string? LatestInvoiceId);

public sealed record CreateMeteredSubscriptionResult(string SubscriptionId, string SubscriptionItemId, string? LatestInvoiceId);

public interface IStripeService
{
    Task<string> EnsureCustomerAsync(long userId, string email, string displayName, CancellationToken ct = default);
    Task<CheckoutResult> CreateCheckoutSessionAsync(CreateCheckoutRequest request, CancellationToken ct = default);
    Task<SetupIntentResult> CreateSetupIntentAsync(Dictionary<string, string>? metadata = null, CancellationToken ct = default);
    Task<string?> ResolvePriceIdByLookupKeyAsync(string lookupKey, CancellationToken ct = default);
    Task<CreateSubscriptionResult> CreateSubscriptionAsync(string customerId, string priceId, string paymentMethodId, int trialDays = 0, Dictionary<string, string>? metadata = null, CancellationToken ct = default);
    Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default);
    Task<List<StripeInvoice>> GetInvoicesAsync(string customerId, int limit = 25, CancellationToken ct = default);

    /// <summary>
    /// Reports a usage quantity (in minutes) to a metered Stripe subscription item.
    /// Uses "increment" action so each report adds to the current period total.
    /// </summary>
    Task ReportUsageAsync(string subscriptionItemId, long minutesUptime, DateTimeOffset timestamp, CancellationToken ct = default);

    /// <summary>
    /// Creates a metered (usage-based) subscription for an Enterprise instance.
    /// Returns the subscription ID and the subscription item ID needed for usage reporting.
    /// </summary>
    Task<CreateMeteredSubscriptionResult> CreateMeteredSubscriptionAsync(
        string customerId, string meteredPriceId, string paymentMethodId,
        int trialDays = 0, Dictionary<string, string>? metadata = null, CancellationToken ct = default);
}
