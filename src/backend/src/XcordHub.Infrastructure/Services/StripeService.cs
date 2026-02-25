using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using XcordHub.Infrastructure.Options;

namespace XcordHub.Infrastructure.Services;

public sealed class StripeService : IStripeService
{
    private readonly StripeOptions _options;
    private readonly ILogger<StripeService> _logger;

    public StripeService(IOptions<StripeOptions> options, ILogger<StripeService> logger)
    {
        _options = options.Value;
        _logger = logger;
        StripeConfiguration.ApiKey = _options.SecretKey;
    }

    public async Task<string> EnsureCustomerAsync(long userId, string email, string displayName, CancellationToken ct = default)
    {
        var service = new CustomerService();

        // Search for existing customer by metadata
        var listOptions = new CustomerListOptions
        {
            Email = email,
            Limit = 1
        };
        var existing = await service.ListAsync(listOptions, cancellationToken: ct);
        if (existing.Data.Count > 0)
        {
            return existing.Data[0].Id;
        }

        // Create new customer
        var createOptions = new CustomerCreateOptions
        {
            Email = email,
            Name = displayName,
            Metadata = new Dictionary<string, string>
            {
                ["hub_user_id"] = userId.ToString()
            }
        };

        var customer = await service.CreateAsync(createOptions, cancellationToken: ct);
        _logger.LogInformation("Created Stripe customer {CustomerId} for user {UserId}", customer.Id, userId);
        return customer.Id;
    }

    public async Task<CheckoutResult> CreateCheckoutSessionAsync(CreateCheckoutRequest request, CancellationToken ct = default)
    {
        var service = new SessionService();

        var options = new SessionCreateOptions
        {
            Customer = request.CustomerId,
            Mode = "subscription",
            LineItems = new List<SessionLineItemOptions>
            {
                new()
                {
                    Price = request.PriceId,
                    Quantity = 1
                }
            },
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            Metadata = new Dictionary<string, string>
            {
                ["instance_id"] = request.InstanceId.ToString()
            }
        };

        var session = await service.CreateAsync(options, cancellationToken: ct);
        _logger.LogInformation("Created Stripe checkout session {SessionId} for instance {InstanceId}",
            session.Id, request.InstanceId);

        return new CheckoutResult(session.Id, session.Url);
    }

    public async Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
    {
        var service = new SubscriptionService();
        await service.CancelAsync(subscriptionId, cancellationToken: ct);
        _logger.LogInformation("Cancelled Stripe subscription {SubscriptionId}", subscriptionId);
    }

    public async Task<List<StripeInvoice>> GetInvoicesAsync(string customerId, int limit = 25, CancellationToken ct = default)
    {
        var service = new InvoiceService();
        var options = new InvoiceListOptions
        {
            Customer = customerId,
            Limit = limit
        };

        var invoices = await service.ListAsync(options, cancellationToken: ct);

        return invoices.Data.Select(i => new StripeInvoice(
            Id: i.Id,
            Description: i.Description,
            AmountCents: i.AmountDue,
            Currency: i.Currency,
            Status: i.Status ?? "unknown",
            CreatedAt: i.Created,
            PdfUrl: i.InvoicePdf
        )).ToList();
    }
}
