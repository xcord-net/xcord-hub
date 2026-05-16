using Microsoft.Extensions.Options;
using Serilog;
using XcordHub.Entities;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Options;

namespace XcordHub.Api;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Verifies that all required Stripe prices exist for each tier/media combination.
    /// Creates any missing prices as recurring monthly prices with the correct amount.
    /// Skipped when Stripe is not configured.
    /// </summary>
    public static async Task EnsureStripePricesAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var stripeOptions = scope.ServiceProvider.GetRequiredService<IOptions<StripeOptions>>().Value;

        if (!stripeOptions.IsConfigured)
        {
            Log.Information("Stripe not configured, skipping price verification");
            return;
        }

        Stripe.StripeConfiguration.ApiKey = stripeOptions.SecretKey;

        var tiers = new[] { InstanceTier.Free, InstanceTier.Basic, InstanceTier.Pro, InstanceTier.Enterprise };
        var priceService = new Stripe.PriceService();
        var productService = new Stripe.ProductService();

        // Ensure a product exists for Xcord hosting
        string productId;
        try
        {
            var products = await productService.ListAsync(new Stripe.ProductListOptions { Limit = 100 });
            var existing = products.Data.FirstOrDefault(p => p.Metadata.ContainsKey("xcord_hosting") && p.Active);
            if (existing != null)
            {
                productId = existing.Id;
            }
            else
            {
                var product = await productService.CreateAsync(new Stripe.ProductCreateOptions
                {
                    Name = "Xcord Hosting",
                    Metadata = new Dictionary<string, string> { ["xcord_hosting"] = "true" }
                });
                productId = product.Id;
                Log.Information("Created Stripe product {ProductId} for Xcord hosting", productId);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to verify Stripe product, skipping price verification");
            return;
        }

        foreach (var tier in tiers)
        {
            foreach (var mediaEnabled in new[] { false, true })
            {
                var priceCents = TierDefaults.GetTotalPriceCents(tier, mediaEnabled);
                if (priceCents == 0 && !mediaEnabled) continue; // Skip free without media

                var lookupKey = BuildStripePriceId(tier, mediaEnabled);

                try
                {
                    // Check if a price with this lookup key already exists
                    var existing = await priceService.ListAsync(new Stripe.PriceListOptions
                    {
                        LookupKeys = new List<string> { lookupKey },
                        Limit = 1
                    });

                    if (existing.Data.Count > 0)
                        continue; // Price exists

                    // Create the price with a lookup key
                    var suffix = mediaEnabled ? " + Media" : "";
                    var created = await priceService.CreateAsync(new Stripe.PriceCreateOptions
                    {
                        Product = productId,
                        Currency = "usd",
                        UnitAmount = priceCents,
                        Recurring = new Stripe.PriceRecurringOptions { Interval = "month" },
                        LookupKey = lookupKey,
                        TransferLookupKey = true,
                        Nickname = $"{tier}{suffix}",
                        Metadata = new Dictionary<string, string>
                        {
                            ["tier"] = tier.ToString(),
                            ["mediaEnabled"] = mediaEnabled.ToString().ToLowerInvariant()
                        }
                    });
                    Log.Information("Created Stripe price {LookupKey} -> {PriceId} ({Amount} cents/mo)",
                        lookupKey, created.Id, priceCents);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to ensure Stripe price {LookupKey}", lookupKey);
                }
            }
        }

        await EnsureEnterpriseMeteredPriceAsync(priceService, productId);

        Log.Information("Stripe price verification completed");
    }

    /// <summary>
    /// Ensure the Enterprise metered price (usage-based, per-minute via Billing Meter).
    /// Used for Enterprise instances that choose metered billing.
    /// Rate: 1 cent per minute ($0.01/min = $0.60/hr ~= $432/mo at 100% uptime).
    /// The price references a BillingMeter named "xcord_instance_uptime_minutes"
    /// which must be set up in the Stripe dashboard (metered billing requires a meter).
    /// </summary>
    private static async Task EnsureEnterpriseMeteredPriceAsync(Stripe.PriceService priceService, string productId)
    {
        const string enterpriseMeteredLookupKey = "price_xcord_enterprise_metered";
        try
        {
            var existingMetered = await priceService.ListAsync(new Stripe.PriceListOptions
            {
                LookupKeys = new List<string> { enterpriseMeteredLookupKey },
                Limit = 1
            });

            if (existingMetered.Data.Count != 0) return;

            // Attempt to look up the meter ID for "xcord_instance_uptime_minutes"
            string? meterId = null;
            try
            {
                var meterService = new Stripe.Billing.MeterService();
                var meters = await meterService.ListAsync(new Stripe.Billing.MeterListOptions { Limit = 100 });
                meterId = meters.Data.FirstOrDefault(m => m.EventName == "xcord_instance_uptime_minutes")?.Id;
            }
            catch (Exception meterEx)
            {
                Log.Warning(meterEx, "Could not retrieve Stripe meters - Enterprise metered price requires a billing meter to be configured manually");
            }

            if (meterId == null)
            {
                Log.Warning(
                    "Stripe metered price {LookupKey} not created: configure a billing meter named " +
                    "'xcord_instance_uptime_minutes' in the Stripe dashboard first",
                    enterpriseMeteredLookupKey);
                return;
            }

            var created = await priceService.CreateAsync(new Stripe.PriceCreateOptions
            {
                Product = productId,
                Currency = "usd",
                UnitAmount = 1, // 1 cent per unit (minute)
                Recurring = new Stripe.PriceRecurringOptions
                {
                    Interval = "month",
                    Meter = meterId,
                    UsageType = "metered"
                },
                LookupKey = enterpriseMeteredLookupKey,
                TransferLookupKey = true,
                Nickname = "Enterprise Metered (per minute)",
                Metadata = new Dictionary<string, string>
                {
                    ["tier"] = "Enterprise",
                    ["billing_type"] = "metered",
                    ["unit"] = "minute"
                }
            });
            Log.Information("Created Stripe metered price {LookupKey} -> {PriceId}",
                enterpriseMeteredLookupKey, created.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to ensure Stripe Enterprise metered price {LookupKey}", enterpriseMeteredLookupKey);
        }
    }

    private static string BuildStripePriceId(InstanceTier tier, bool mediaEnabled)
    {
        var suffix = mediaEnabled ? "_media" : "";
        return $"price_xcord_{tier.ToString().ToLowerInvariant()}{suffix}";
    }
}
