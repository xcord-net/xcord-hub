import { A } from '@solidjs/router';
import { createResource, createSignal, For, onMount, Show } from 'solid-js';
import ContactModal from '../../components/ContactModal';
import PageMeta from '../../components/PageMeta';

interface InstanceBillingItem {
  instanceId: string;
  domain: string;
  displayName: string;
  tier: string;
  mediaEnabled: boolean;
  priceCents: number;
  billingStatus: string;
}

interface BillingData {
  instances: InstanceBillingItem[];
}

interface InvoiceSummary {
  id: string;
  description: string;
  amountCents: number;
  currency: string;
  status: string;
  createdAt: string;
  pdfUrl: string | null;
}

interface InvoicesData {
  invoices: InvoiceSummary[];
}

type Tier = 'Free' | 'Basic' | 'Pro' | 'Enterprise';

const TIER_CONFIG: Record<Tier, { maxUsers: number; label: string }> = {
  Free:       { maxUsers: 10,  label: 'Free' },
  Basic:      { maxUsers: 50,  label: 'Basic' },
  Pro:        { maxUsers: 200, label: 'Pro' },
  Enterprise: { maxUsers: 500, label: 'Enterprise' },
};

const TIER_PRICE_CENTS: Record<Tier, number> = {
  Free: 0,
  Basic: 6000,
  Pro: 15000,
  Enterprise: 30000,
};

const TIER_MEDIA_CENTS: Record<Tier, number> = {
  Free: 400,
  Basic: 300,
  Pro: 200,
  Enterprise: 100,
};

function authHeaders(): HeadersInit {
  const token = localStorage.getItem('xcord_hub_token');
  return token ? { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' } : {};
}

async function fetchBilling(): Promise<BillingData> {
  const res = await fetch('/api/v1/hub/billing', { headers: authHeaders() });
  if (!res.ok) throw new Error('Failed to load billing info');
  return res.json();
}

interface UsageData {
  instanceId: string;
  domain: string;
  tier: string;
  isMeteredBilling: boolean;
  totalUptimeMinutes: number;
  totalUptimeHours: number;
  uptimePercentage: number;
  estimatedCostCents: number;
}

async function fetchUsage(instanceId: string): Promise<UsageData> {
  const res = await fetch(`/api/v1/hub/instances/${instanceId}/usage`, { headers: authHeaders() });
  if (!res.ok) throw new Error('Failed to load usage');
  return res.json();
}

async function fetchInvoices(): Promise<InvoicesData> {
  const res = await fetch('/api/v1/hub/billing/invoices', { headers: authHeaders() });
  if (!res.ok) throw new Error('Failed to load invoices');
  return res.json();
}

function formatAmount(cents: number, currency: string): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: currency.toUpperCase(),
  }).format(cents / 100);
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });
}

function statusBadge(status: string): string {
  const classes: Record<string, string> = {
    Active: 'bg-xcord-green/10 text-xcord-green',
    PastDue: 'bg-yellow-500/10 text-yellow-400',
    Suspended: 'bg-xcord-red/10 text-xcord-red',
    Cancelled: 'bg-xcord-bg-tertiary text-xcord-text-muted',
  };
  return classes[status] ?? 'bg-xcord-bg-tertiary text-xcord-text-muted';
}

function formatPrice(cents: number): string {
  if (cents === 0) return 'Free';
  const dollars = cents / 100;
  const formatted = dollars % 1 === 0 ? `$${dollars}` : `$${dollars.toFixed(2)}`;
  return `${formatted}/mo`;
}

function formatTier(tier: string, media: boolean): string {
  return media ? `${tier} + Media` : tier;
}

function computeTotalCents(tier: Tier, mediaEnabled: boolean): number {
  const base = TIER_PRICE_CENTS[tier];
  const maxUsers = TIER_CONFIG[tier].maxUsers;
  const mediaCents = mediaEnabled ? TIER_MEDIA_CENTS[tier] * maxUsers : 0;
  return base + mediaCents;
}

function UsageBreakdown(props: { instanceId: string }) {
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

function PlanEditor(props: {
  instance: InstanceBillingItem;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [showContact, setShowContact] = createSignal(false);
  const [paymentsEnabled, setPaymentsEnabled] = createSignal(false);
  const [selectedTier, setSelectedTier] = createSignal<Tier>(props.instance.tier as Tier);
  const [mediaEnabled, setMediaEnabled] = createSignal(props.instance.mediaEnabled);
  const [saving, setSaving] = createSignal(false);
  const [saveError, setSaveError] = createSignal('');
  const [notifyTier, setNotifyTier] = createSignal<string | null>(null);
  const [notifyEmail, setNotifyEmail] = createSignal('');
  const [notifyStatus, setNotifyStatus] = createSignal<'idle' | 'loading' | 'success' | 'error'>('idle');
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
      <div class="bg-xcord-bg-primary rounded-xl w-full max-w-lg max-h-[90vh] overflow-y-auto">
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
              {/* Free - always selectable */}
              <button
                type="button"
                onClick={() => setSelectedTier('Free')}
                class={tierButtonClass('Free')}
              >
                <div class="font-semibold">Free</div>
                <div class="text-xs text-xcord-text-muted mt-1">Up to 10 members</div>
              </button>

              {/* Basic */}
              <Show
                when={paymentsEnabled()}
                fallback={
                  <button
                    type="button"
                    onClick={() => { setNotifyTier('Basic'); setNotifyStatus('idle'); setNotifyMessage(''); setNotifyEmail(''); }}
                    class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center opacity-60 cursor-default hover:opacity-80 transition"
                  >
                    <div class="font-semibold">Basic</div>
                    <div class="text-xs text-xcord-brand mt-1">Coming soon</div>
                  </button>
                }
              >
                <button
                  type="button"
                  onClick={() => setSelectedTier('Basic')}
                  class={tierButtonClass('Basic')}
                >
                  <div class="font-semibold">Basic</div>
                  <div class="text-xs text-xcord-text-muted mt-1">Up to 50 members</div>
                  <div class="text-xs text-xcord-text-muted mt-1">$60/mo</div>
                </button>
              </Show>

              {/* Pro */}
              <Show
                when={paymentsEnabled()}
                fallback={
                  <button
                    type="button"
                    onClick={() => { setNotifyTier('Pro'); setNotifyStatus('idle'); setNotifyMessage(''); setNotifyEmail(''); }}
                    class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center opacity-60 cursor-default hover:opacity-80 transition"
                  >
                    <div class="font-semibold">Pro</div>
                    <div class="text-xs text-xcord-brand mt-1">Coming soon</div>
                  </button>
                }
              >
                <button
                  type="button"
                  onClick={() => setSelectedTier('Pro')}
                  class={tierButtonClass('Pro')}
                >
                  <div class="font-semibold">Pro</div>
                  <div class="text-xs text-xcord-text-muted mt-1">Up to 200 members</div>
                  <div class="text-xs text-xcord-text-muted mt-1">$150/mo</div>
                </button>
              </Show>

              {/* Enterprise - always contact us */}
              <button
                type="button"
                onClick={() => setShowContact(true)}
                class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center opacity-60 hover:opacity-80 transition"
              >
                <div class="font-semibold">Enterprise</div>
                <div class="text-xs text-xcord-brand mt-1">Contact us</div>
              </button>
            </div>
          </div>

          {/* Media toggle */}
          <div class="mb-5">
            <div class="flex items-center gap-3">
              <div>
                <Show
                  when={paymentsEnabled()}
                  fallback={
                    <>
                      <span class="text-sm font-medium text-xcord-text-primary">Voice &amp; Video</span>
                      <button
                        type="button"
                        onClick={() => { setNotifyTier('Voice & Video'); setNotifyStatus('idle'); setNotifyMessage(''); setNotifyEmail(''); }}
                        class="text-xs text-xcord-brand ml-2 hover:underline"
                      >
                        Coming soon
                      </button>
                      <div class="text-xs text-xcord-text-muted mt-0.5">
                        Voice channels, video calls, screen share
                      </div>
                    </>
                  }
                >
                  <div class="flex items-center gap-3">
                    <div>
                      <span class="text-sm font-medium text-xcord-text-primary">Voice &amp; Video</span>
                      <div class="text-xs text-xcord-text-muted mt-0.5">
                        Voice channels, video calls, screen share
                      </div>
                    </div>
                    <button
                      type="button"
                      role="switch"
                      aria-checked={mediaEnabled()}
                      onClick={() => setMediaEnabled(v => !v)}
                      class={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-xcord-brand ${mediaEnabled() ? 'bg-xcord-brand' : 'bg-xcord-bg-accent'}`}
                    >
                      <span
                        class={`inline-block h-3 w-3 rounded-full bg-white transition-transform ${mediaEnabled() ? 'translate-x-5' : 'translate-x-1'}`}
                      />
                    </button>
                  </div>
                </Show>
              </div>
            </div>
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

          {/* Actions */}
          <div class="flex gap-3 justify-end">
            <button
              onClick={props.onClose}
              class="px-4 py-2 text-sm text-xcord-text-muted hover:text-xcord-text-primary transition"
            >
              Cancel
            </button>
            <Show when={hasChanges()}>
              <button
                onClick={handleSave}
                disabled={saving()}
                class="px-4 py-2 text-sm bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-white rounded font-medium transition"
              >
                {saving() ? 'Saving...' : 'Save Changes'}
              </button>
            </Show>
          </div>
        </div>
      </div>

      {/* Notify-me modal */}
      <Show when={notifyTier()}>
        <div class="fixed inset-0 bg-black/60 flex items-center justify-center z-60 p-4" onClick={(e) => { if (e.target === e.currentTarget) setNotifyTier(null); }}>
          <div class="bg-xcord-bg-primary rounded-xl w-full max-w-sm p-6">
            <div class="flex items-center justify-between mb-4">
              <h3 class="text-lg font-bold text-xcord-text-primary">{notifyTier()}</h3>
              <button onClick={() => setNotifyTier(null)} class="text-xcord-text-muted hover:text-xcord-text-primary text-xl leading-none">&times;</button>
            </div>
            <Show when={notifyStatus() !== 'success'} fallback={<p class="text-sm text-xcord-green py-4 text-center">{notifyMessage()}</p>}>
              <p class="text-sm text-xcord-text-muted mb-4">We'll let you know when {notifyTier()} is available.</p>
              <form onSubmit={handleNotify} class="space-y-3">
                <input type="email" required placeholder="you@example.com" value={notifyEmail()} onInput={(e) => setNotifyEmail(e.currentTarget.value)} class="w-full px-3 py-2 bg-xcord-bg-tertiary text-xcord-text-primary rounded border-none outline-none focus:ring-2 focus:ring-xcord-brand text-sm" />
                <button type="submit" disabled={notifyStatus() === 'loading'} class="w-full py-2 bg-xcord-brand hover:bg-xcord-brand-hover disabled:opacity-50 text-white rounded font-medium transition text-sm">
                  {notifyStatus() === 'loading' ? 'Submitting...' : 'Notify Me'}
                </button>
              </form>
              <Show when={notifyStatus() === 'error'}>
                <p class="text-xs text-xcord-red mt-2">{notifyMessage()}</p>
              </Show>
            </Show>
          </div>
        </div>
      </Show>

      <ContactModal open={showContact()} onClose={() => setShowContact(false)} />
    </div>
  );
}

export default function Billing() {
  const [billing, { refetch: refetchBilling }] = createResource(fetchBilling);
  const [invoices] = createResource(fetchInvoices);
  const [editingInstance, setEditingInstance] = createSignal<InstanceBillingItem | null>(null);

  return (
    <>
      <PageMeta
        title="Billing - Xcord"
        description="Manage your billing and subscriptions."
        path="/dashboard/billing"
        noindex
      />
      <div class="p-8 max-w-3xl">
      <h1 data-testid="billing-heading" class="text-2xl font-bold text-xcord-text-primary mb-8">Billing</h1>

      {/* Instance list loading / error */}
      <Show when={billing.loading}>
        <div class="text-xcord-text-muted text-sm py-4">Loading billing info...</div>
      </Show>

      <Show when={billing.error}>
        <div class="bg-xcord-red/10 text-xcord-red text-sm rounded-lg p-4 mb-6">
          Failed to load billing information. Please refresh the page.
        </div>
      </Show>

      <Show when={billing()}>
        {(data) => (
          <div class="mb-8">
            <Show
              when={data().instances.length > 0}
              fallback={
                <div class="bg-xcord-bg-secondary rounded-lg p-8 text-center">
                  <p class="text-xcord-text-muted text-sm mb-2">
                    No instances yet. Create one to get started.
                  </p>
                  <A
                    href="/dashboard/create-instance"
                    class="text-sm text-xcord-text-link hover:underline"
                  >
                    Create an instance &rarr;
                  </A>
                </div>
              }
            >
              <div class="space-y-4">
                <For each={data().instances}>
                  {(instance) => (
                    <div class="bg-xcord-bg-secondary rounded-lg p-6">
                      <div class="flex items-start justify-between mb-3">
                        <div>
                          <div class="text-base font-semibold text-xcord-text-primary">
                            {instance.displayName}
                          </div>
                          <div class="text-xs text-xcord-text-muted mt-0.5">{instance.domain}</div>
                        </div>
                        <span
                          class={`px-3 py-1 text-sm font-medium rounded ${statusBadge(instance.billingStatus)}`}
                        >
                          {instance.billingStatus}
                        </span>
                      </div>

                      <div class="grid grid-cols-3 gap-4 mb-4">
                        <div>
                          <div class="text-xs text-xcord-text-muted mb-1">Plan</div>
                          <div class="text-sm text-xcord-text-primary font-medium">
                            {formatTier(instance.tier, instance.mediaEnabled)}
                          </div>
                        </div>
                        <div>
                          <div class="text-xs text-xcord-text-muted mb-1">User Limit</div>
                          <div class="text-sm text-xcord-text-primary font-medium">
                            {TIER_CONFIG[instance.tier as Tier]?.maxUsers ?? '-'} users
                          </div>
                        </div>
                        <div>
                          <div class="text-xs text-xcord-text-muted mb-1">Price</div>
                          <div class="text-sm text-xcord-text-primary font-medium">
                            {formatPrice(instance.priceCents)}
                          </div>
                        </div>
                      </div>

                      {/* Usage breakdown for Enterprise metered instances */}
                      <Show when={instance.tier === 'Enterprise'}>
                        <UsageBreakdown instanceId={instance.instanceId} />
                      </Show>

                      <div class="pt-3 border-t border-xcord-bg-tertiary">
                        <button
                          onClick={() => setEditingInstance(instance)}
                          class="px-3 py-1.5 text-xs font-medium text-xcord-text-link bg-xcord-brand/10 rounded hover:bg-xcord-brand/20 transition"
                        >
                          Change Plan
                        </button>
                      </div>
                    </div>
                  )}
                </For>
              </div>
            </Show>
          </div>
        )}
      </Show>

      {/* Plan editor modal */}
      <Show when={editingInstance()}>
        {(instance) => (
          <PlanEditor
            instance={instance()}
            onClose={() => setEditingInstance(null)}
            onSaved={() => { refetchBilling(); }}
          />
        )}
      </Show>

      {/* Invoices */}
      <div class="bg-xcord-bg-secondary rounded-lg p-6">
        <h2 class="text-lg font-semibold text-xcord-text-primary mb-4">Invoices</h2>

        <Show when={invoices.loading}>
          <div class="text-xcord-text-muted text-sm py-4">Loading invoices...</div>
        </Show>

        <Show when={invoices.error}>
          <div class="text-xcord-red text-sm">Failed to load invoices.</div>
        </Show>

        <Show when={invoices() && invoices()!.invoices.length === 0}>
          <div class="text-center py-8">
            <p class="text-xcord-text-muted text-sm">No invoices yet.</p>
            <p class="text-xcord-text-muted text-xs mt-1">
              Invoices will appear here once you have an active paid subscription.
            </p>
          </div>
        </Show>

        <Show when={invoices() && invoices()!.invoices.length > 0}>
          <div class="divide-y divide-xcord-bg-tertiary">
            <For each={invoices()!.invoices}>
              {(invoice) => (
                <div class="flex items-center justify-between py-3">
                  <div>
                    <div class="text-sm text-xcord-text-primary">{invoice.description}</div>
                    <div class="text-xs text-xcord-text-muted mt-0.5">{formatDate(invoice.createdAt)}</div>
                  </div>
                  <div class="flex items-center gap-4">
                    <span class={`text-xs px-2 py-0.5 rounded ${statusBadge(invoice.status)}`}>
                      {invoice.status}
                    </span>
                    <span class="text-sm font-medium text-xcord-text-primary">
                      {formatAmount(invoice.amountCents, invoice.currency)}
                    </span>
                    <Show when={invoice.pdfUrl}>
                      <a
                        href={invoice.pdfUrl!}
                        target="_blank"
                        rel="noopener noreferrer"
                        class="text-xs text-xcord-text-link hover:underline"
                      >
                        PDF
                      </a>
                    </Show>
                  </div>
                </div>
              )}
            </For>
          </div>
        </Show>
      </div>
    </div>
    </>
  );
}
