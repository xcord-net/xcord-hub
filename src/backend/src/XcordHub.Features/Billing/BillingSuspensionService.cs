using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Billing;

/// <summary>
/// Suspends instances for non-payment and resumes them once payment arrives.
/// Used by the <see cref="BillingEnforcer"/> (suspend) and the Stripe webhook
/// handler (resume). The <see cref="InstanceBilling.BillingSuspended"/> flag
/// distinguishes billing suspensions from manual ones, so a payment webhook can
/// never resume an instance an admin suspended for other reasons.
/// </summary>
public sealed class BillingSuspensionService(
    HubDbContext dbContext,
    IDockerService dockerService,
    IInstanceNotifier instanceNotifier,
    ILogger<BillingSuspensionService> logger)
{
    public async Task SuspendForNonPaymentAsync(long instanceId, CancellationToken cancellationToken)
    {
        var instance = await dbContext.ManagedInstances
            .Include(i => i.Infrastructure)
            .Include(i => i.Billing)
            .FirstOrDefaultAsync(i => i.Id == instanceId && i.DeletedAt == null, cancellationToken);

        if (instance?.Billing == null || instance.Status != InstanceStatus.Running)
        {
            return;
        }

        logger.LogWarning(
            "Suspending instance {InstanceId} ({Domain}) for non-payment (billing status {Status} since {Since})",
            instance.Id, instance.Domain, instance.Billing.BillingStatus, instance.Billing.BillingStatusChangedAt);

        await instanceNotifier.NotifyShuttingDownAsync(
            instance.Domain,
            "suspended for non-payment",
            cancellationToken);

        if (instance.Infrastructure != null)
        {
            await dockerService.StopContainerAsync(
                instance.Infrastructure.DockerContainerId,
                cancellationToken);
        }

        instance.Status = InstanceStatus.Suspended;
        instance.Billing.BillingSuspended = true;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resumes an instance that was suspended for non-payment, after its billing
    /// returned to Active. No-op for instances suspended manually.
    /// </summary>
    public async Task ResumeAfterPaymentAsync(InstanceBilling billing, CancellationToken cancellationToken)
    {
        if (!billing.BillingSuspended)
        {
            return;
        }

        var instance = await dbContext.ManagedInstances
            .Include(i => i.Infrastructure)
            .FirstOrDefaultAsync(i => i.Id == billing.ManagedInstanceId && i.DeletedAt == null, cancellationToken);

        billing.BillingSuspended = false;

        if (instance == null || instance.Status != InstanceStatus.Suspended)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        logger.LogInformation(
            "Resuming instance {InstanceId} ({Domain}) after payment",
            instance.Id, instance.Domain);

        if (instance.Infrastructure != null)
        {
            var isRunning = await dockerService.VerifyContainerRunningAsync(
                instance.Infrastructure.DockerContainerId,
                cancellationToken);

            if (!isRunning)
            {
                logger.LogWarning(
                    "Instance {InstanceId} container not running after billing resume, may need manual intervention",
                    instance.Id);
            }
        }

        instance.Status = InstanceStatus.Running;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
