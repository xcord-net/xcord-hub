namespace XcordHub.Features.Upgrades;

public interface IUpgradeQueue
{
    ValueTask EnqueueInstanceUpgradeAsync(long instanceId, string targetImage, long? rolloutId = null, CancellationToken cancellationToken = default);
    ValueTask EnqueueRolloutAsync(long rolloutId, bool force = false, CancellationToken cancellationToken = default);
    ValueTask<UpgradeWorkItem> DequeueAsync(CancellationToken cancellationToken);
}

public sealed record UpgradeWorkItem
{
    // Exactly one of these is set
    public InstanceUpgradeRequest? InstanceUpgrade { get; init; }
    public RolloutRequest? Rollout { get; init; }
}

public sealed record InstanceUpgradeRequest(long InstanceId, string TargetImage, long? RolloutId = null);
public sealed record RolloutRequest(long RolloutId, bool Force = false);
