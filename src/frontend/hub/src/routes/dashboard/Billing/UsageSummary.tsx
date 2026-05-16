import { createResource, Show } from 'solid-js';
import { fetchUsage, formatPrice } from './types';

export default function UsageSummary(props: { instanceId: string }) {
  const [usage] = createResource(() => props.instanceId, fetchUsage);

  return (
    <Show when={usage() && usage()!.isMeteredBilling}>
      <div class="bg-xcord-bg-tertiary rounded-lg p-4 mt-3 mb-3">
        <div class="text-xs font-bold uppercase text-xcord-text-muted mb-2">Usage (Last 30 Days)</div>
        <div class="grid grid-cols-4 gap-3">
          <div>
            <div class="text-xs text-xcord-text-muted">Uptime</div>
            <div class="text-sm font-medium text-xcord-text-primary">{usage()!.totalUptimeHours}h</div>
          </div>
          <div>
            <div class="text-xs text-xcord-text-muted">Availability</div>
            <div class="text-sm font-medium text-xcord-text-primary">{usage()!.uptimePercentage}%</div>
          </div>
          <div>
            <div class="text-xs text-xcord-text-muted">Est. Cost</div>
            <div class="text-sm font-medium text-xcord-text-primary">{formatPrice(usage()!.estimatedCostCents)}</div>
          </div>
          <div>
            <div class="text-xs text-xcord-text-muted">Billing</div>
            <div class="text-sm font-medium text-xcord-text-primary">Metered</div>
          </div>
        </div>
      </div>
    </Show>
  );
}
