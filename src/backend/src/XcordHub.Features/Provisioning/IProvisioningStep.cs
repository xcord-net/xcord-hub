using XcordHub;

namespace XcordHub.Features.Provisioning;

public interface IProvisioningStep
{
    string StepName { get; }
    Task<Result<bool>> ExecuteAsync(long instanceId, CancellationToken cancellationToken = default);
    Task<Result<bool>> VerifyAsync(long instanceId, CancellationToken cancellationToken = default);
}
