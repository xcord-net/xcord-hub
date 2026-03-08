using System.Threading.Channels;

namespace XcordHub.Features.Upgrades;

public sealed class UpgradeQueue : IUpgradeQueue
{
    private readonly Channel<UpgradeWorkItem> _channel = Channel.CreateUnbounded<UpgradeWorkItem>(
        new UnboundedChannelOptions { SingleReader = true });

    public async ValueTask EnqueueInstanceUpgradeAsync(long instanceId, string targetImage, long? rolloutId = null, CancellationToken cancellationToken = default)
    {
        var item = new UpgradeWorkItem
        {
            InstanceUpgrade = new InstanceUpgradeRequest(instanceId, targetImage, rolloutId)
        };

        await _channel.Writer.WriteAsync(item, cancellationToken);
    }

    public async ValueTask EnqueueRolloutAsync(long rolloutId, bool force = false, CancellationToken cancellationToken = default)
    {
        var item = new UpgradeWorkItem
        {
            Rollout = new RolloutRequest(rolloutId, force)
        };

        await _channel.Writer.WriteAsync(item, cancellationToken);
    }

    public async ValueTask<UpgradeWorkItem> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _channel.Reader.ReadAsync(cancellationToken);
    }
}
