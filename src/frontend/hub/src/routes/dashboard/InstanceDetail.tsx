import { createSignal, onMount, Show } from 'solid-js';
import { A, useParams } from '@solidjs/router';

interface InstanceInfo {
  id: string;
  subdomain: string;
  displayName: string;
  domain: string;
  status: string;
  tier: string;
  memberCount?: number;
  storageUsedMb?: number;
  createdAt?: string;
}

export default function InstanceDetail() {
  const params = useParams();
  const [instance, setInstance] = createSignal<InstanceInfo | null>(null);
  const [loading, setLoading] = createSignal(true);
  const [editName, setEditName] = createSignal('');
  const [saving, setSaving] = createSignal(false);
  const [confirmDelete, setConfirmDelete] = createSignal(false);
  const [actionLoading, setActionLoading] = createSignal(false);
  const [message, setMessage] = createSignal('');

  const token = () => localStorage.getItem('xcord_hub_token');

  onMount(async () => {
    try {
      const response = await fetch(`/api/v1/hub/instances/${params.id}`, {
        headers: token() ? { Authorization: `Bearer ${token()}` } : {},
      });
      if (response.ok) {
        const data = await response.json();
        setInstance(data);
        setEditName(data.displayName);
      }
    } catch {
      // API may not be available
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
      default: return 'bg-xcord-status-offline';
    }
  };

  const handleSaveName = async () => {
    if (!instance()) return;
    setSaving(true);
    try {
      const response = await fetch(`/api/v1/hub/instances/${params.id}`, {
        method: 'PATCH',
        headers: {
          'Content-Type': 'application/json',
          ...(token() ? { Authorization: `Bearer ${token()}` } : {}),
        },
        body: JSON.stringify({ displayName: editName() }),
      });
      if (response.ok) {
        setInstance({ ...instance()!, displayName: editName() });
        setMessage('Name updated');
        setTimeout(() => setMessage(''), 3000);
      }
    } catch {
      setMessage('Failed to update');
    } finally {
      setSaving(false);
    }
  };

  const handleAction = async (action: 'suspend' | 'resume' | 'destroy') => {
    setActionLoading(true);
    try {
      const response = await fetch(`/api/v1/hub/instances/${params.id}/${action}`, {
        method: 'POST',
        headers: token() ? { Authorization: `Bearer ${token()}` } : {},
      });
      if (response.ok) {
        const data = await response.json();
        setInstance(data);
        setConfirmDelete(false);
        setMessage(`Instance ${action}ed`);
        setTimeout(() => setMessage(''), 3000);
      }
    } catch {
      setMessage(`Failed to ${action}`);
    } finally {
      setActionLoading(false);
    }
  };

  return (
    <div class="p-8 max-w-3xl">
      <A href="/dashboard" class="text-sm text-xcord-text-link hover:underline mb-4 inline-block">
        &larr; Back to Overview
      </A>

      <Show
        when={!loading()}
        fallback={<div class="text-xcord-text-muted">Loading...</div>}
      >
        <Show
          when={instance()}
          fallback={<div class="text-xcord-text-muted">Instance not found</div>}
        >
          {(inst) => (
            <>
              <div class="flex items-center gap-4 mb-8">
                <div class="w-14 h-14 rounded-lg bg-xcord-brand/20 flex items-center justify-center text-xcord-brand text-2xl font-bold">
                  {inst().displayName[0].toUpperCase()}
                </div>
                <div>
                  <h1 class="text-2xl font-bold text-xcord-text-primary">{inst().displayName}</h1>
                  <div class="flex items-center gap-2 text-sm text-xcord-text-muted">
                    <div class={`w-2 h-2 rounded-full ${statusDot(inst().status)}`} />
                    <span class="capitalize">{inst().status}</span>
                    <span>&middot;</span>
                    <span>{inst().domain}</span>
                  </div>
                </div>
              </div>

              <Show when={message()}>
                <div class="mb-4 px-4 py-2 bg-xcord-brand/10 text-xcord-brand text-sm rounded">{message()}</div>
              </Show>

              {/* Info Grid */}
              <div class="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8">
                <div class="bg-xcord-bg-secondary rounded-lg p-4">
                  <div class="text-xs text-xcord-text-muted mb-1">Domain</div>
                  <div class="text-sm text-xcord-text-primary font-mono">{inst().domain}</div>
                </div>
                <div class="bg-xcord-bg-secondary rounded-lg p-4">
                  <div class="text-xs text-xcord-text-muted mb-1">Status</div>
                  <div class="text-sm text-xcord-text-primary capitalize">{inst().status}</div>
                </div>
                <div class="bg-xcord-bg-secondary rounded-lg p-4">
                  <div class="text-xs text-xcord-text-muted mb-1">Members</div>
                  <div class="text-sm text-xcord-text-primary">{inst().memberCount ?? '—'}</div>
                </div>
                <div class="bg-xcord-bg-secondary rounded-lg p-4">
                  <div class="text-xs text-xcord-text-muted mb-1">Tier</div>
                  <div class="text-sm text-xcord-text-primary capitalize">{inst().tier}</div>
                </div>
              </div>

              {/* Edit Name */}
              <div class="bg-xcord-bg-secondary rounded-lg p-6 mb-6">
                <h2 class="text-lg font-semibold text-xcord-text-primary mb-4">Settings</h2>
                <div class="flex gap-3">
                  <div class="flex-1">
                    <label class="block text-xs font-bold uppercase text-xcord-text-muted mb-2">Display Name</label>
                    <input
                      type="text"
                      value={editName()}
                      onInput={(e) => setEditName(e.currentTarget.value)}
                      class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-0 outline-none focus:ring-2 focus:ring-xcord-brand"
                    />
                  </div>
                  <button
                    onClick={handleSaveName}
                    disabled={saving() || editName() === inst().displayName}
                    class="self-end px-4 py-2 bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-white rounded text-sm font-medium transition"
                  >
                    {saving() ? 'Saving...' : 'Save'}
                  </button>
                </div>
              </div>

              {/* Danger Zone */}
              <div class="bg-xcord-bg-secondary rounded-lg p-6 border border-xcord-red/20">
                <h2 class="text-lg font-semibold text-xcord-red mb-4">Danger Zone</h2>
                <div class="space-y-3">
                  <Show when={inst().status.toLowerCase() === 'running'}>
                    <button
                      onClick={() => handleAction('suspend')}
                      disabled={actionLoading()}
                      class="px-4 py-2 bg-xcord-yellow/10 text-xcord-yellow hover:bg-xcord-yellow/20 rounded text-sm font-medium transition"
                    >
                      Suspend Instance
                    </button>
                  </Show>
                  <Show when={inst().status.toLowerCase() === 'suspended'}>
                    <button
                      onClick={() => handleAction('resume')}
                      disabled={actionLoading()}
                      class="px-4 py-2 bg-xcord-green/10 text-xcord-green hover:bg-xcord-green/20 rounded text-sm font-medium transition"
                    >
                      Resume Instance
                    </button>
                  </Show>

                  <Show when={!confirmDelete()}>
                    <button
                      onClick={() => setConfirmDelete(true)}
                      class="px-4 py-2 bg-xcord-red/10 text-xcord-red hover:bg-xcord-red/20 rounded text-sm font-medium transition"
                    >
                      Delete Instance
                    </button>
                  </Show>
                  <Show when={confirmDelete()}>
                    <div class="flex items-center gap-3 p-3 bg-xcord-red/10 rounded">
                      <span class="text-sm text-xcord-red">Are you sure? This cannot be undone.</span>
                      <button
                        onClick={() => handleAction('destroy')}
                        disabled={actionLoading()}
                        class="px-3 py-1 bg-xcord-red text-white rounded text-sm font-medium transition hover:opacity-80"
                      >
                        Confirm Delete
                      </button>
                      <button
                        onClick={() => setConfirmDelete(false)}
                        class="px-3 py-1 bg-xcord-bg-accent text-xcord-text-secondary rounded text-sm transition"
                      >
                        Cancel
                      </button>
                    </div>
                  </Show>
                </div>
              </div>
            </>
          )}
        </Show>
      </Show>
    </div>
  );
}
