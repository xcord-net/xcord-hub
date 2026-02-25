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

public interface IStripeService
{
    Task<string> EnsureCustomerAsync(long userId, string email, string displayName, CancellationToken ct = default);
    Task<CheckoutResult> CreateCheckoutSessionAsync(CreateCheckoutRequest request, CancellationToken ct = default);
    Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default);
    Task<List<StripeInvoice>> GetInvoicesAsync(string customerId, int limit = 25, CancellationToken ct = default);
}
