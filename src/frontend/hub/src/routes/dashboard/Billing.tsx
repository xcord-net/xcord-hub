import { A } from '@solidjs/router';
import { createResource, createSignal, For, Show } from 'solid-js';

interface InstanceBillingItem {
  instanceId: string;
  domain: string;
  displayName: string;
  featureTier: string;
  userCountTier: string;
  hdUpgrade: boolean;
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

interface ChangePlanResponse {
  featureTier: string;
  userCountTier: string;
  priceCents: number;
  checkoutUrl: string | null;
  requiresCheckout: boolean;
}

type FeatureTier = 'Chat' | 'Audio' | 'Video';
type UserCountTier = 'Tier10' | 'Tier50' | 'Tier100' | 'Tier500';

// Price matrix matching backend TierDefaults.GetPriceCents (in cents)
const PRICE_MATRIX: Record<FeatureTier, Record<UserCountTier, number>> = {
  Chat:  { Tier10: 0,    Tier50: 2000, Tier100: 6000,  Tier500: 20000 },
  Audio: { Tier10: 2000, Tier50: 4500, Tier100: 11000, Tier500: 40000 },
  Video: { Tier10: 4000, Tier50: 7000, Tier100: 16000, Tier500: 55000 },
};

const HD_UPGRADE_PRICE: Record<UserCountTier, number> = {
  Tier10: 2500, Tier50: 5000, Tier100: 7500, Tier500: 15000,
};

function getPriceCents(feature: FeatureTier, users: UserCountTier, hd: boolean): number {
  const base = PRICE_MATRIX[feature]?.[users] ?? 0;
  const hdCost = (hd && feature === 'Video') ? (HD_UPGRADE_PRICE[users] ?? 0) : 0;
  return base + hdCost;
}

function authHeaders(): HeadersInit {
  const token = localStorage.getItem('xcord_hub_token');
  return token ? { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' } : {};
}

async function fetchBilling(): Promise<BillingData> {
  const res = await fetch('/api/v1/hub/billing', { headers: authHeaders() });
  if (!res.ok) throw new Error('Failed to load billing info');
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

function formatUserCount(tier: string): string {
  const map: Record<string, string> = {
    Tier10: '10 users',
    Tier50: '50 users',
    Tier100: '100 users',
    Tier500: '500 users',
  };
  return map[tier] ?? tier;
}

function formatFeature(tier: string, hd: boolean): string {
  const map: Record<string, string> = {
    Chat: 'Chat',
    Audio: 'Chat + Audio',
    Video: 'Chat + Audio + Video',
  };
  const base = map[tier] ?? tier;
  return hd && tier === 'Video' ? `${base} + HD` : base;
}

const USER_TIERS: [UserCountTier, string][] = [
  ['Tier10', '10'],
  ['Tier50', '50'],
  ['Tier100', '100'],
  ['Tier500', '500'],
];

const SALES_EMAIL = 'sales@xcord.net';

const FEATURE_TIERS: { id: FeatureTier; label: string; desc: string }[] = [
  { id: 'Chat', label: 'Chat', desc: 'Text only' },
  { id: 'Audio', label: '+Audio', desc: 'Text + voice' },
  { id: 'Video', label: '+Video', desc: 'Text + voice + video' },
];

function PlanEditor(props: {
  instance: InstanceBillingItem;
  onClose: () => void;
  onSuccess: () => void;
}) {
  const [feature, setFeature] = createSignal<FeatureTier>(props.instance.featureTier as FeatureTier);
  const [users, setUsers] = createSignal<UserCountTier>(props.instance.userCountTier as UserCountTier);
  const [hd, setHd] = createSignal(props.instance.hdUpgrade);
  const [loading, setLoading] = createSignal(false);
  const [error, setError] = createSignal('');
  const [confirmingDowngrade, setConfirmingDowngrade] = createSignal(false);
  const [confirmingCancel, setConfirmingCancel] = createSignal(false);
  const [customTier, setCustomTier] = createSignal(false);

  const newPrice = () => getPriceCents(feature(), users(), hd());
  const currentPrice = () => props.instance.priceCents;
  const isChanged = () =>
    feature() !== props.instance.featureTier ||
    users() !== props.instance.userCountTier ||
    hd() !== props.instance.hdUpgrade;
  const isUpgrade = () => newPrice() > currentPrice();
  const isDowngrade = () => newPrice() < currentPrice();
  const isFree = () => newPrice() === 0;

  // Reset HD when not on Video tier
  const handleFeatureChange = (tier: FeatureTier) => {
    setFeature(tier);
    if (tier !== 'Video') setHd(false);
  };

  const lostFeatures = (): string[] => {
    const lost: string[] = [];
    const cur = props.instance.featureTier;
    const next = feature();
    if ((cur === 'Video' || cur === 'Audio') && next === 'Chat')
      lost.push('Voice channels');
    if (cur === 'Video' && next !== 'Video')
      lost.push('Video channels');
    if (props.instance.hdUpgrade && !hd())
      lost.push('HD video, simulcast, recording');
    const curUsers = parseInt(props.instance.userCountTier.replace('Tier', ''));
    const nextUsers = parseInt(users().replace('Tier', ''));
    if (nextUsers < curUsers)
      lost.push(`User capacity reduced from ${curUsers} to ${nextUsers}`);
    return lost;
  };

  const submitChange = async () => {
    setLoading(true);
    setError('');
    try {
      const res = await fetch(
        `/api/v1/hub/instances/${props.instance.instanceId}/billing/change`,
        {
          method: 'POST',
          headers: authHeaders(),
          body: JSON.stringify({
            instanceId: parseInt(props.instance.instanceId),
            targetFeatureTier: feature(),
            targetUserCountTier: users(),
            hdUpgrade: hd(),
          }),
        }
      );

      if (!res.ok) {
        const err = await res.json().catch(() => null);
        throw new Error(err?.detail || err?.message || 'Failed to change plan');
      }

      const data: ChangePlanResponse = await res.json();

      if (data.requiresCheckout && data.checkoutUrl) {
        window.location.href = data.checkoutUrl;
        return;
      }

      props.onSuccess();
    } catch (err: any) {
      setError(err?.message || 'Failed to change plan');
    } finally {
      setLoading(false);
    }
  };

  const submitCancel = async () => {
    setLoading(true);
    setError('');
    try {
      const res = await fetch(
        `/api/v1/hub/instances/${props.instance.instanceId}/billing/cancel`,
        {
          method: 'POST',
          headers: authHeaders(),
        }
      );

      if (!res.ok) {
        const err = await res.json().catch(() => null);
        throw new Error(err?.detail || err?.message || 'Failed to cancel subscription');
      }

      props.onSuccess();
    } catch (err: any) {
      setError(err?.message || 'Failed to cancel subscription');
    } finally {
      setLoading(false);
    }
  };

  const handleApply = () => {
    if (isDowngrade() && lostFeatures().length > 0 && !confirmingDowngrade()) {
      setConfirmingDowngrade(true);
      return;
    }
    submitChange();
  };

  return (
    <div class="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
      <div class="bg-xcord-bg-primary rounded-xl w-full max-w-lg max-h-[90vh] overflow-y-auto">
        <div class="p-6">
          <div class="flex items-center justify-between mb-6">
            <h2 class="text-lg font-bold text-xcord-text-primary">
              Change Plan — {props.instance.displayName}
            </h2>
            <button
              onClick={props.onClose}
              class="text-xcord-text-muted hover:text-xcord-text-primary text-xl leading-none"
            >
              &times;
            </button>
          </div>

          {/* Feature tier */}
          <div class="mb-5">
            <p class="text-xs font-bold uppercase text-xcord-text-muted mb-2">Features</p>
            <div class="grid grid-cols-3 gap-2">
              <For each={FEATURE_TIERS}>
                {(tier) => (
                  <button
                    type="button"
                    onClick={() => handleFeatureChange(tier.id)}
                    disabled={loading()}
                    class={`px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center transition ${
                      feature() === tier.id
                        ? 'ring-2 ring-xcord-brand'
                        : 'hover:bg-xcord-bg-accent'
                    }`}
                  >
                    <div class="font-semibold">{tier.label}</div>
                    <div class="text-xs text-xcord-text-muted mt-1">{tier.desc}</div>
                  </button>
                )}
              </For>
            </div>
          </div>

          {/* User count */}
          <div class="mb-5">
            <p class="text-xs font-bold uppercase text-xcord-text-muted mb-2">User Capacity</p>
            <div class="flex gap-2">
              <For each={USER_TIERS}>
                {([value, label]) => (
                  <button
                    type="button"
                    onClick={() => { setUsers(value); setCustomTier(false); }}
                    disabled={loading()}
                    class={`px-4 py-1.5 rounded-full text-sm font-medium transition ${
                      users() === value && !customTier()
                        ? 'ring-2 ring-xcord-brand bg-xcord-bg-tertiary text-xcord-text-primary'
                        : 'bg-xcord-bg-tertiary text-xcord-text-muted hover:text-xcord-text-primary'
                    }`}
                  >
                    {label}
                  </button>
                )}
              </For>
              <button
                type="button"
                onClick={() => setCustomTier(true)}
                disabled={loading()}
                class={`px-4 py-1.5 rounded-full text-sm font-medium transition ${
                  customTier()
                    ? 'ring-2 ring-xcord-brand bg-xcord-bg-tertiary text-xcord-text-primary'
                    : 'bg-xcord-bg-tertiary text-xcord-text-muted hover:text-xcord-text-primary'
                }`}
              >
                Custom
              </button>
            </div>
          </div>

          {/* HD upgrade toggle */}
          <Show when={feature() === 'Video'}>
            <div class="mb-5">
              <label class="flex items-center gap-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={hd()}
                  onChange={(e) => setHd(e.currentTarget.checked)}
                  disabled={loading()}
                  class="w-4 h-4 rounded border-xcord-bg-tertiary text-xcord-brand focus:ring-xcord-brand"
                />
                <div>
                  <span class="text-sm font-medium text-xcord-text-primary">HD Upgrade</span>
                  <span class="text-xs text-xcord-text-muted ml-2">
                    +{formatPrice(HD_UPGRADE_PRICE[users()] ?? 0)}
                  </span>
                  <div class="text-xs text-xcord-text-muted mt-0.5">
                    1080p video, simulcast, recording
                  </div>
                </div>
              </label>
            </div>
          </Show>

          {/* Contact Sales for custom tier */}
          <Show when={customTier()}>
            <div class="bg-xcord-brand/5 border border-xcord-brand/20 rounded-lg p-5 mb-5">
              <p class="text-sm font-semibold text-xcord-text-primary mb-2">
                Need more than 500 users?
              </p>
              <p class="text-xs text-xcord-text-muted mb-4">
                Custom plans include dedicated infrastructure, priority support,
                SLA guarantees, and flexible user limits. Contact our sales team
                to discuss your requirements.
              </p>
              <a
                href={`mailto:${SALES_EMAIL}?subject=${encodeURIComponent(`Custom plan inquiry — ${props.instance.displayName}`)}`}
                class="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-xcord-brand rounded hover:bg-xcord-brand-hover transition"
              >
                Contact Sales
              </a>
            </div>
          </Show>

          {/* Price summary */}
          <Show when={!customTier()}>
          <div class="bg-xcord-bg-secondary rounded-lg p-4 mb-5">
            <div class="flex items-center justify-between mb-2">
              <span class="text-xs text-xcord-text-muted">Current plan</span>
              <span class="text-sm text-xcord-text-muted">{formatPrice(currentPrice())}</span>
            </div>
            <div class="flex items-center justify-between">
              <span class="text-xs text-xcord-text-muted">New plan</span>
              <span class="text-sm font-bold text-xcord-text-primary">{formatPrice(newPrice())}</span>
            </div>
            <Show when={isChanged() && newPrice() !== currentPrice()}>
              <div class="border-t border-xcord-bg-tertiary mt-2 pt-2">
                <div class="flex items-center justify-between">
                  <span class="text-xs text-xcord-text-muted">Difference</span>
                  <span
                    class={`text-sm font-medium ${
                      isUpgrade() ? 'text-xcord-green' : 'text-yellow-400'
                    }`}
                  >
                    {isUpgrade() ? '+' : ''}{formatPrice(newPrice() - currentPrice()).replace('/mo', '')}/mo
                  </span>
                </div>
              </div>
            </Show>
          </div>

          {/* Downgrade warning */}
          <Show when={confirmingDowngrade()}>
            <div class="bg-yellow-500/10 border border-yellow-500/20 rounded-lg p-4 mb-5">
              <p class="text-sm font-medium text-yellow-400 mb-2">
                Downgrading will remove:
              </p>
              <ul class="text-xs text-yellow-300 space-y-1">
                <For each={lostFeatures()}>
                  {(item) => <li>- {item}</li>}
                </For>
              </ul>
              <p class="text-xs text-yellow-400 mt-2">
                This takes effect immediately. Are you sure?
              </p>
            </div>
          </Show>

          {/* Cancel subscription confirmation */}
          <Show when={confirmingCancel()}>
            <div class="bg-xcord-red/10 border border-xcord-red/20 rounded-lg p-4 mb-5">
              <p class="text-sm font-medium text-xcord-red mb-1">
                Cancel subscription?
              </p>
              <p class="text-xs text-xcord-red/80">
                This will downgrade your instance to the free plan (Chat + 10 users).
                All paid features will be removed immediately.
              </p>
            </div>
          </Show>

          <Show when={error()}>
            <div class="text-sm text-xcord-red mb-4">{error()}</div>
          </Show>

          {/* Actions */}
          <div class="flex gap-3">
            <button
              onClick={props.onClose}
              disabled={loading()}
              class="px-4 py-2 text-sm text-xcord-text-muted hover:text-xcord-text-primary transition"
            >
              Cancel
            </button>

            <div class="flex-1" />

            {/* Cancel subscription button */}
            <Show when={currentPrice() > 0 && !confirmingCancel()}>
              <button
                onClick={() => setConfirmingCancel(true)}
                disabled={loading()}
                class="px-4 py-2 text-sm font-medium text-xcord-red bg-xcord-red/10 rounded hover:bg-xcord-red/20 transition"
              >
                Cancel Subscription
              </button>
            </Show>

            <Show when={confirmingCancel()}>
              <button
                onClick={() => setConfirmingCancel(false)}
                disabled={loading()}
                class="px-4 py-2 text-sm text-xcord-text-muted hover:text-xcord-text-primary transition"
              >
                Keep Plan
              </button>
              <button
                onClick={submitCancel}
                disabled={loading()}
                class="px-4 py-2 text-sm font-medium text-white bg-xcord-red rounded hover:bg-xcord-red/80 transition disabled:opacity-50"
              >
                {loading() ? 'Cancelling...' : 'Confirm Cancel'}
              </button>
            </Show>

            {/* Apply change button */}
            <Show when={isChanged() && !confirmingCancel()}>
              <button
                onClick={handleApply}
                disabled={loading()}
                class={`px-4 py-2 text-sm font-medium text-white rounded transition disabled:opacity-50 ${
                  confirmingDowngrade()
                    ? 'bg-yellow-600 hover:bg-yellow-700'
                    : 'bg-xcord-brand hover:bg-xcord-brand-hover'
                }`}
              >
                {loading()
                  ? 'Applying...'
                  : confirmingDowngrade()
                    ? 'Confirm Downgrade'
                    : isUpgrade()
                      ? isFree()
                        ? 'Apply Change'
                        : 'Upgrade'
                      : 'Change Plan'}
              </button>
            </Show>
          </div>
          </Show>
        </div>
      </div>
    </div>
  );
}

export default function Billing() {
  const [billing, { refetch }] = createResource(fetchBilling);
  const [invoices, { refetch: refetchInvoices }] = createResource(fetchInvoices);
  const [editingInstance, setEditingInstance] = createSignal<InstanceBillingItem | null>(null);

  const handlePlanSuccess = () => {
    setEditingInstance(null);
    refetch();
    refetchInvoices();
  };

  return (
    <div class="p-8 max-w-3xl">
      <h1 class="text-2xl font-bold text-xcord-text-primary mb-8">Billing</h1>

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
                          <div class="text-xs text-xcord-text-muted mb-1">Features</div>
                          <div class="text-sm text-xcord-text-primary font-medium">
                            {formatFeature(instance.featureTier, instance.hdUpgrade)}
                          </div>
                        </div>
                        <div>
                          <div class="text-xs text-xcord-text-muted mb-1">User Limit</div>
                          <div class="text-sm text-xcord-text-primary font-medium">
                            {formatUserCount(instance.userCountTier)}
                          </div>
                        </div>
                        <div>
                          <div class="text-xs text-xcord-text-muted mb-1">Price</div>
                          <div class="text-sm text-xcord-text-primary font-medium">
                            {formatPrice(instance.priceCents)}
                          </div>
                        </div>
                      </div>

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
            onSuccess={handlePlanSuccess}
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
  );
}
