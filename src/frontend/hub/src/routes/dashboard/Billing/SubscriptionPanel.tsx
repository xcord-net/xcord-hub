import { createSignal, onMount, Show } from 'solid-js';
import ContactModal from '../../../components/ContactModal';
import NotifyModal, { type NotifyStatus } from './NotifyModal';
import { computeTotalCents, formatPrice, type InstanceBillingItem, type Tier } from './types';

interface SubscriptionPanelProps {
  instance: InstanceBillingItem;
  onClose: () => void;
  onSaved: () => void;
}

export default function SubscriptionPanel(props: SubscriptionPanelProps) {
  const [showContact, setShowContact] = createSignal(false);
  const [paymentsEnabled, setPaymentsEnabled] = createSignal(false);
  const [selectedTier, setSelectedTier] = createSignal<Tier>(props.instance.tier as Tier);
  const [mediaEnabled, setMediaEnabled] = createSignal(props.instance.mediaEnabled);
  const [saving, setSaving] = createSignal(false);
  const [saveError, setSaveError] = createSignal('');
  const [notifyTier, setNotifyTier] = createSignal<string | null>(null);
  const [notifyEmail, setNotifyEmail] = createSignal('');
  const [notifyStatus, setNotifyStatus] = createSignal<NotifyStatus>('idle');
  const [notifyMessage, setNotifyMessage] = createSignal('');

  onMount(async () => {
    try {
      const res = await fetch('/api/v1/hub/features');
      if (res.ok) {
        const data = await res.json();
        setPaymentsEnabled(data.paymentsEnabled ?? false);
      }
    } catch {
      // leave paymentsEnabled as false
    }
  });

  const hasChanges = () =>
    selectedTier() !== props.instance.tier || mediaEnabled() !== props.instance.mediaEnabled;

  const tierButtonClass = (tier: Tier) => {
    const isSelected = selectedTier() === tier;
    return `px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center hover:bg-xcord-bg-accent transition${isSelected ? ' ring-2 ring-xcord-brand' : ''}`;
  };

  const handleSave = async () => {
    setSaveError('');
    setSaving(true);
    try {
      const token = localStorage.getItem('xcord_hub_token');
      const headers: Record<string, string> = { 'Content-Type': 'application/json' };
      if (token) headers['Authorization'] = `Bearer ${token}`;
      const res = await fetch(`/api/v1/hub/instances/${props.instance.instanceId}/billing/change`, {
        method: 'POST',
        headers,
        body: JSON.stringify({ targetTier: selectedTier(), mediaEnabled: mediaEnabled() }),
      });
      const data = await res.json();
      if (!res.ok) {
        setSaveError(data.detail ?? data.message ?? 'Failed to update plan');
        return;
      }
      if (data.requiresCheckout && data.checkoutUrl) {
        window.location.href = data.checkoutUrl;
        return;
      }
      props.onSaved();
      props.onClose();
    } catch {
      setSaveError('Network error. Please try again.');
    } finally {
      setSaving(false);
    }
  };

  const openNotify = (tier: string) => {
    setNotifyTier(tier);
    setNotifyStatus('idle');
    setNotifyMessage('');
    setNotifyEmail('');
  };

  const handleNotify = async (e: Event) => {
    e.preventDefault();
    const tier = notifyTier();
    if (!tier) return;
    setNotifyStatus('loading');
    try {
      const res = await fetch('/api/v1/mailing-list', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: notifyEmail(), tier }),
      });
      const data = await res.json();
      if (!res.ok) {
        setNotifyStatus('error');
        setNotifyMessage(data.message ?? 'Something went wrong.');
      } else {
        setNotifyStatus('success');
        setNotifyMessage(data.message);
        setTimeout(() => {
          setNotifyTier(null);
          setNotifyEmail('');
          setNotifyStatus('idle');
          setNotifyMessage('');
        }, 3000);
      }
    } catch {
      setNotifyStatus('error');
      setNotifyMessage('Network error. Please try again.');
    }
  };

  return (
    <div class="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
      <div class="bg-xcord-bg-primary rounded-lg w-full max-w-lg max-h-[90vh] overflow-y-auto">
        <div class="p-6">
          <div class="flex items-center justify-between mb-6">
            <h2 class="text-lg font-bold text-xcord-text-primary">
              Change Plan - {props.instance.displayName}
            </h2>
            <button
              onClick={props.onClose}
              class="text-xcord-text-muted hover:text-xcord-text-primary text-xl leading-none"
            >
              &times;
            </button>
          </div>

          {/* Tier selector */}
          <div class="mb-5">
            <p class="text-xs font-bold uppercase text-xcord-text-muted mb-2">Plan</p>
            <div class="grid grid-cols-4 gap-2">
              <button type="button" onClick={() => setSelectedTier('Free')} class={tierButtonClass('Free')}>
                <div class="font-semibold">Free</div>
                <div class="text-xs text-xcord-text-muted mt-1">Up to 10 members</div>
              </button>

              <Show
                when={paymentsEnabled()}
                fallback={
                  <button type="button" onClick={() => openNotify('Basic')} class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center opacity-60 cursor-default hover:opacity-80 transition">
                    <div class="font-semibold">Basic</div>
                    <div class="text-xs text-xcord-brand mt-1">Coming soon</div>
                  </button>
                }
              >
                <button type="button" onClick={() => setSelectedTier('Basic')} class={tierButtonClass('Basic')}>
                  <div class="font-semibold">Basic</div>
                  <div class="text-xs text-xcord-text-muted mt-1">Up to 50 members</div>
                  <div class="text-xs text-xcord-text-muted mt-1">$60/mo</div>
                </button>
              </Show>

              <Show
                when={paymentsEnabled()}
                fallback={
                  <button type="button" onClick={() => openNotify('Pro')} class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center opacity-60 cursor-default hover:opacity-80 transition">
                    <div class="font-semibold">Pro</div>
                    <div class="text-xs text-xcord-brand mt-1">Coming soon</div>
                  </button>
                }
              >
                <button type="button" onClick={() => setSelectedTier('Pro')} class={tierButtonClass('Pro')}>
                  <div class="font-semibold">Pro</div>
                  <div class="text-xs text-xcord-text-muted mt-1">Up to 200 members</div>
                  <div class="text-xs text-xcord-text-muted mt-1">$150/mo</div>
                </button>
              </Show>

              <button type="button" onClick={() => setShowContact(true)} class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center opacity-60 hover:opacity-80 transition">
                <div class="font-semibold">Enterprise</div>
                <div class="text-xs text-xcord-brand mt-1">Contact us</div>
              </button>
            </div>
          </div>

          {/* Media toggle */}
          <div class="mb-5 flex items-center gap-3">
            <Show when={paymentsEnabled()} fallback={
              <div>
                <span class="text-sm font-medium text-xcord-text-primary">Voice &amp; Video</span>
                <button type="button" onClick={() => openNotify('Voice & Video')} class="text-xs text-xcord-brand ml-2 hover:underline">Coming soon</button>
                <div class="text-xs text-xcord-text-muted mt-0.5">Voice channels, video calls, screen share</div>
              </div>
            }>
              <div>
                <span class="text-sm font-medium text-xcord-text-primary">Voice &amp; Video</span>
                <div class="text-xs text-xcord-text-muted mt-0.5">Voice channels, video calls, screen share</div>
              </div>
              <button type="button" role="switch" aria-checked={mediaEnabled()} onClick={() => setMediaEnabled(v => !v)}
                class={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-xcord-brand ${mediaEnabled() ? 'bg-xcord-brand' : 'bg-xcord-bg-accent'}`}>
                <span class={`inline-block h-3 w-3 rounded-full bg-white transition-transform ${mediaEnabled() ? 'translate-x-5' : 'translate-x-1'}`} />
              </button>
            </Show>
          </div>

          {/* Price summary */}
          <div class="bg-xcord-bg-secondary rounded-lg p-4 mb-5">
            <div class="flex items-center justify-between">
              <span class="text-xs font-medium text-xcord-text-primary">Total</span>
              <span class="text-sm font-bold text-xcord-text-primary">
                {formatPrice(computeTotalCents(selectedTier(), mediaEnabled()))}
              </span>
            </div>
          </div>

          <Show when={saveError()}>
            <div class="text-sm text-xcord-red mb-4">{saveError()}</div>
          </Show>

          <div class="flex gap-3 justify-end">
            <button onClick={props.onClose} class="px-4 py-2 text-sm text-xcord-text-muted hover:text-xcord-text-primary transition">
              Cancel
            </button>
            <Show when={hasChanges()}>
              <button onClick={handleSave} disabled={saving()} class="px-4 py-2 text-sm bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-xcord-landing-bg rounded font-medium transition">
                {saving() ? 'Saving...' : 'Save Changes'}
              </button>
            </Show>
          </div>
        </div>
      </div>

      <NotifyModal
        notifyTier={notifyTier}
        setNotifyTier={setNotifyTier}
        notifyEmail={notifyEmail}
        setNotifyEmail={setNotifyEmail}
        notifyStatus={notifyStatus}
        notifyMessage={notifyMessage}
        onSubmit={handleNotify}
      />

      <ContactModal open={showContact()} onClose={() => setShowContact(false)} />
    </div>
  );
}
