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

    public async Task<SetupIntentResult> CreateSetupIntentAsync(Dictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        var service = new SetupIntentService();
        var options = new SetupIntentCreateOptions
        {
            PaymentMethodTypes = new List<string> { "card" },
            Metadata = metadata ?? new Dictionary<string, string>()
        };

        var intent = await service.CreateAsync(options, cancellationToken: ct);
        _logger.LogInformation("Created Stripe SetupIntent {SetupIntentId}", intent.Id);

        return new SetupIntentResult(intent.Id, intent.ClientSecret);
    }

    public async Task<string?> ResolvePriceIdByLookupKeyAsync(string lookupKey, CancellationToken ct = default)
    {
        var service = new PriceService();
        var prices = await service.ListAsync(new PriceListOptions
        {
            LookupKeys = new List<string> { lookupKey },
            Limit = 1
        }, cancellationToken: ct);

        return prices.Data.Count > 0 ? prices.Data[0].Id : null;
    }

    public async Task<CreateSubscriptionResult> CreateSubscriptionAsync(
        string customerId, string priceId, string paymentMethodId,
        int trialDays = 0, Dictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        // Attach payment method to customer
        var pmService = new PaymentMethodService();
        await pmService.AttachAsync(paymentMethodId, new PaymentMethodAttachOptions
        {
            Customer = customerId,
        }, cancellationToken: ct);

        // Set as default payment method
        var customerService = new CustomerService();
        await customerService.UpdateAsync(customerId, new CustomerUpdateOptions
        {
            InvoiceSettings = new CustomerInvoiceSettingsOptions
            {
                DefaultPaymentMethod = paymentMethodId,
            }
        }, cancellationToken: ct);

        // Create subscription with a trial period - no charge until trial ends
        var subService = new SubscriptionService();
        var sub = await subService.CreateAsync(new SubscriptionCreateOptions
        {
            Customer = customerId,
            Items = new List<SubscriptionItemOptions>
            {
                new() { Price = priceId }
            },
            DefaultPaymentMethod = paymentMethodId,
            TrialPeriodDays = trialDays > 0 ? trialDays : null,
            Metadata = metadata ?? new Dictionary<string, string>(),
        }, cancellationToken: ct);

        _logger.LogInformation("Created Stripe subscription {SubscriptionId} for customer {CustomerId}",
            sub.Id, customerId);

        return new CreateSubscriptionResult(sub.Id, sub.LatestInvoiceId);
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

    public async Task ReportUsageAsync(string subscriptionItemId, long minutesUptime, DateTimeOffset timestamp, CancellationToken ct = default)
    {
        // In Stripe SDK v50+, metered billing uses Billing Meter events.
        // The subscriptionItemId here is used as a unique customer identifier for the meter event.
        // The event name "xcord_instance_uptime_minutes" must match the meter configured in Stripe.
        var service = new Stripe.Billing.MeterEventService();
        await service.CreateAsync(new Stripe.Billing.MeterEventCreateOptions
        {
            EventName = "xcord_instance_uptime_minutes",
            Payload = new Dictionary<string, string>
            {
                ["value"] = minutesUptime.ToString(),
                ["stripe_customer_id"] = subscriptionItemId // This is actually the customer ID in meter context
            },
            Timestamp = timestamp.UtcDateTime
        }, cancellationToken: ct);

        _logger.LogInformation(
            "Reported {Minutes} uptime minutes to Stripe for subscription item {SubscriptionItemId}",
            minutesUptime, subscriptionItemId);
    }

    public async Task<CreateMeteredSubscriptionResult> CreateMeteredSubscriptionAsync(
        string customerId, string meteredPriceId, string paymentMethodId,
        int trialDays = 0, Dictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        // Attach payment method to customer
        var pmService = new PaymentMethodService();
        await pmService.AttachAsync(paymentMethodId, new PaymentMethodAttachOptions
        {
            Customer = customerId,
        }, cancellationToken: ct);

        // Set as default payment method
        var customerService = new CustomerService();
        await customerService.UpdateAsync(customerId, new CustomerUpdateOptions
        {
            InvoiceSettings = new CustomerInvoiceSettingsOptions
            {
                DefaultPaymentMethod = paymentMethodId,
            }
        }, cancellationToken: ct);

        var subService = new SubscriptionService();
        var sub = await subService.CreateAsync(new SubscriptionCreateOptions
        {
            Customer = customerId,
            Items = new List<SubscriptionItemOptions>
            {
                new() { Price = meteredPriceId }
            },
            DefaultPaymentMethod = paymentMethodId,
            TrialPeriodDays = trialDays > 0 ? trialDays : null,
            Metadata = metadata ?? new Dictionary<string, string>(),
        }, cancellationToken: ct);

        // The subscription item ID is needed for usage reporting
        var subItemId = sub.Items.Data.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException("Stripe subscription created without items");

        _logger.LogInformation(
            "Created metered Stripe subscription {SubscriptionId} (item {SubItemId}) for customer {CustomerId}",
            sub.Id, subItemId, customerId);

        return new CreateMeteredSubscriptionResult(sub.Id, subItemId, sub.LatestInvoiceId);
    }
}
