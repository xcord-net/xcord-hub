using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Features.Billing;
using XcordHub.Features.Instances;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Monitoring;

/// <summary>
/// Enforces payment for billed instances. Without this, BillingStatus is purely
/// informational: a paid instance whose subscription never materialized
/// (AwaitingPayment) or whose payments fail (PastDue) would run forever for free.
/// Instances in either state past the grace period are suspended; payment
/// webhooks resume them via <see cref="BillingSuspensionService"/>.
/// </summary>
public sealed class BillingEnforcer(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<BillingEnforcer> logger) : PollingBackgroundService(serviceScopeFactory, logger)
{
    internal static readonly TimeSpan GracePeriod = TimeSpan.FromDays(7);

    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task ProcessAsync(CancellationToken ct)
    {
        using var scope = ServiceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var suspensionService = scope.ServiceProvider.GetRequiredService<BillingSuspensionService>();

        await EnforceAsync(dbContext, suspensionService, ct);
    }

    internal async Task EnforceAsync(
        HubDbContext dbContext,
        BillingSuspensionService suspensionService,
        CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - GracePeriod;

        var candidates = await dbContext.InstanceBillings
            .AsNoTracking()
            .Include(b => b.ManagedInstance)
            .Where(b => !b.BillingExempt
                && (b.BillingStatus == BillingStatus.AwaitingPayment || b.BillingStatus == BillingStatus.PastDue)
                && b.BillingStatusChangedAt < cutoff
                && b.ManagedInstance.Status == InstanceStatus.Running
                && b.ManagedInstance.DeletedAt == null)
            .ToListAsync(ct);

        // Tier pricing is not SQL-translatable; filter zero-priced records here.
        var delinquent = candidates
            .Where(b => TierDefaults.GetTotalPriceCents(b.Tier, b.MediaEnabled) > 0)
            .ToList();

        if (delinquent.Count == 0)
        {
            return;
        }

        Logger.LogWarning(
            "BillingEnforcer found {Count} delinquent instances past the {Grace} grace period",
            delinquent.Count, GracePeriod);

        foreach (var billing in delinquent)
        {
            try
            {
                await suspensionService.SuspendForNonPaymentAsync(billing.ManagedInstanceId, ct);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "Failed to suspend delinquent instance {InstanceId}",
                    billing.ManagedInstanceId);
            }
        }
    }
}
