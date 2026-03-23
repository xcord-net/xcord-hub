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

public interface IStripeService
{
    Task<string> EnsureCustomerAsync(long userId, string email, string displayName, CancellationToken ct = default);
    Task<CheckoutResult> CreateCheckoutSessionAsync(CreateCheckoutRequest request, CancellationToken ct = default);
    Task<SetupIntentResult> CreateSetupIntentAsync(Dictionary<string, string>? metadata = null, CancellationToken ct = default);
    Task<string?> ResolvePriceIdByLookupKeyAsync(string lookupKey, CancellationToken ct = default);
    Task<CreateSubscriptionResult> CreateSubscriptionAsync(string customerId, string priceId, string paymentMethodId, Dictionary<string, string>? metadata = null, CancellationToken ct = default);
    Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default);
    Task<List<StripeInvoice>> GetInvoicesAsync(string customerId, int limit = 25, CancellationToken ct = default);
}
