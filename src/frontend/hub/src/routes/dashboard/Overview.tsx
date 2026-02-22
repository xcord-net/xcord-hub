import { createSignal, onMount, Show, For } from 'solid-js';
import { A } from '@solidjs/router';
import { instanceStore, type ConnectedInstance } from '../../stores/instance.store';

interface InstanceInfo {
  id: string;
  subdomain: string;
  displayName: string;
  domain: string;
  status: string;
  tier: string;
  memberCount?: number;
  storageUsedMb?: number;
}

export default function Overview() {
  const [instances, setInstances] = createSignal<InstanceInfo[]>([]);
  const [loading, setLoading] = createSignal(true);

  onMount(async () => {
    try {
      const token = localStorage.getItem('xcord_hub_token');
      const response = await fetch('/api/v1/hub/instances', {
        headers: token ? { Authorization: `Bearer ${token}` } : {},
      });
      if (response.ok) {
        setInstances(await response.json());
      }
    } catch {
      // API may not be available yet
    } finally {
      setLoading(false);
    }
  });

  const statusDot = (status: string) => {
    switch (status.toLowerCase()) {
      case 'running': return 'bg-xcord-status-online';
      case 'pending':
      case 'provisioning': return 'bg-xcord-status-idle';
      case 'suspended':
      case 'failed': return 'bg-xcord-status-dnd';
      case 'destroyed': return 'bg-xcord-status-offline';
      default: return 'bg-xcord-status-offline';
    }
  };

  const connectedCount = () => instanceStore.connectedInstances().length;

  return (
    <div class="p-8">
      <div class="flex items-center justify-between mb-8">
        <h1 class="text-2xl font-bold text-xcord-text-primary">Overview</h1>
        <A
          href="/dashboard/create"
          class="px-4 py-2 bg-xcord-brand hover:bg-xcord-brand-hover text-white rounded text-sm font-medium transition"
        >
          Create Instance
        </A>
      </div>

      {/* Stats grid */}
      <div class="grid grid-cols-1 md:grid-cols-3 gap-4 mb-8">
        <div class="bg-xcord-bg-secondary rounded-lg p-5">
          <div class="text-sm text-xcord-text-muted mb-1">Your Instances</div>
          <div class="text-2xl font-bold text-xcord-text-primary">{instances().length}</div>
        </div>
        <div class="bg-xcord-bg-secondary rounded-lg p-5">
          <div class="text-sm text-xcord-text-muted mb-1">Connected Servers</div>
          <div class="text-2xl font-bold text-xcord-text-primary">{connectedCount()}</div>
        </div>
        <div class="bg-xcord-bg-secondary rounded-lg p-5">
          <div class="text-sm text-xcord-text-muted mb-1">Total Members</div>
          <div class="text-2xl font-bold text-xcord-text-primary">
            {instances().reduce((sum, i) => sum + (i.memberCount || 0), 0)}
          </div>
        </div>
      </div>

      {/* Instance list */}
      <h2 class="text-lg font-semibold text-xcord-text-primary mb-4">Your Instances</h2>
      <Show
        when={!loading()}
        fallback={
          <div class="bg-xcord-bg-secondary rounded-lg p-8 text-center text-xcord-text-muted">
            Loading instances...
          </div>
        }
      >
        <Show
          when={instances().length > 0}
          fallback={
            <div class="bg-xcord-bg-secondary rounded-lg p-8 text-center">
              <p class="text-xcord-text-muted mb-4">You haven't created any instances yet.</p>
              <A
                href="/dashboard/create"
                class="inline-block px-6 py-2 bg-xcord-brand hover:bg-xcord-brand-hover text-white rounded text-sm font-medium transition"
              >
                Create Your First Instance
              </A>
            </div>
          }
        >
          <div class="space-y-2">
            <For each={instances()}>
              {(instance) => (
                <A
                  href={`/dashboard/instances/${instance.id}`}
                  class="flex items-center gap-4 bg-xcord-bg-secondary hover:bg-xcord-bg-accent rounded-lg p-4 transition group"
                >
                  <div class="w-10 h-10 rounded-lg bg-xcord-brand/20 flex items-center justify-center text-xcord-brand font-bold shrink-0">
                    {instance.displayName[0].toUpperCase()}
                  </div>
                  <div class="flex-1 min-w-0">
                    <div class="font-medium text-xcord-text-primary group-hover:text-white transition">
                      {instance.displayName}
                    </div>
                    <div class="text-sm text-xcord-text-muted">{instance.domain}</div>
                  </div>
                  <div class="flex items-center gap-2">
                    <div class={`w-2 h-2 rounded-full ${statusDot(instance.status)}`} />
                    <span class="text-sm text-xcord-text-muted capitalize">{instance.status}</span>
                  </div>
                  <span class="text-xs text-xcord-text-muted bg-xcord-bg-tertiary px-2 py-1 rounded">{instance.tier}</span>
                </A>
              )}
            </For>
          </div>
        </Show>
      </Show>
    </div>
  );
}
