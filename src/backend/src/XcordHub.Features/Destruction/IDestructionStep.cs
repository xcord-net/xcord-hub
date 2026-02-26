using XcordHub.Entities;

namespace XcordHub.Features.Destruction;

public interface IDestructionStep
{
    string StepName { get; }
    Task ExecuteAsync(ManagedInstance instance, InstanceInfrastructure infrastructure, CancellationToken cancellationToken = default);
}
