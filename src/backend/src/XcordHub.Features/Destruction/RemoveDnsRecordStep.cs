using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Destruction;

public sealed class RemoveDnsRecordStep(IDnsProvider dnsProvider, ILogger<RemoveDnsRecordStep> logger) : IDestructionStep
{
    public string StepName => "RemoveDnsRecord";

    public async Task ExecuteAsync(ManagedInstance instance, InstanceInfrastructure infrastructure, CancellationToken cancellationToken)
    {
        logger.LogInformation("Removing DNS record for {Domain}", instance.Domain);
        await dnsProvider.DeleteARecordAsync(instance.Domain, cancellationToken);
    }
}
