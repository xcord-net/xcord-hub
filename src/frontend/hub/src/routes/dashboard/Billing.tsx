import { A } from '@solidjs/router';
import { createSignal, createResource, For, Show, Switch, Match } from 'solid-js';

interface BillingTierFeature {
  name: string;
  value: string;
}

interface BillingTierInfo {
  name: string;
  price: string;
  period: string;
  maxInstances: number;
  maxUsersPerInstance: number;
  maxStorageMb: number;
  features: BillingTierFeature[];
}

interface BillingData {
  tier: string;
  status: string;
  hasStripeSubscription: boolean;
  currentPeriodEnd: string | null;
  nextBillingDate: string | null;
  instanceCount: number;
  maxInstances: number;
  currentTierInfo: BillingTierInfo;
  availableTiers: BillingTierInfo[];
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

function authHeaders(): HeadersInit {
  const token = localStorage.getItem('xcord_hub_token');
  return token ? { Authorization: `Bearer ${token}` } : {};
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

function formatStorage(mb: number): string {
  if (mb < 0) return 'Unlimited';
  if (mb >= 1024) return `${mb / 1024} GB`;
  return `${mb} MB`;
}

function formatInstances(n: number): string {
  return n < 0 ? 'Unlimited' : String(n);
}

function formatUsers(n: number): string {
  return n < 0 ? 'Unlimited' : n.toLocaleString();
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

function statusBadge(status: string) {
  const classes: Record<string, string> = {
    Active: 'bg-xcord-green/10 text-xcord-green',
    PastDue: 'bg-yellow-500/10 text-yellow-400',
    Suspended: 'bg-xcord-red/10 text-xcord-red',
    Cancelled: 'bg-xcord-bg-tertiary text-xcord-text-muted',
  };
  return classes[status] ?? 'bg-xcord-bg-tertiary text-xcord-text-muted';
}

export default function Billing() {
  const [billing, { refetch: refetchBilling }] = createResource(fetchBilling);
  const [invoices] = createResource(fetchInvoices);

  const [upgrading, setUpgrading] = createSignal<string | null>(null);
  const [upgradeError, setUpgradeError] = createSignal<string | null>(null);
  const [upgradeSuccess, setUpgradeSuccess] = createSignal<string | null>(null);

  const [showCancelConfirm, setShowCancelConfirm] = createSignal(false);
  const [cancelling, setCancelling] = createSignal(false);
  const [cancelError, setCancelError] = createSignal<string | null>(null);

  const handleUpgrade = async (targetTier: string) => {
    setUpgradeError(null);
    setUpgradeSuccess(null);
    setUpgrading(targetTier);
    try {
      const res = await fetch('/api/v1/hub/billing/upgrade', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...authHeaders() },
        body: JSON.stringify({ targetTier }),
      });
      const data = await res.json();
      if (!res.ok) {
        setUpgradeError(data?.detail || data?.message || 'Failed to change plan');
        return;
      }
      if (data.requiresCheckout && data.checkoutUrl) {
        window.location.href = data.checkoutUrl;
        return;
      }
      if (targetTier === 'Enterprise') {
        setUpgradeSuccess('Contact us at billing@xcord.net to discuss an Enterprise plan.');
      } else {
        setUpgradeSuccess(`Plan changed to ${data.tier}.`);
        refetchBilling();
      }
    } catch {
      setUpgradeError('Network error. Please try again.');
    } finally {
      setUpgrading(null);
    }
  };

  const handleCancel = async () => {
    setCancelError(null);
    setCancelling(true);
    try {
      const res = await fetch('/api/v1/hub/billing/cancel', {
        method: 'POST',
        headers: authHeaders(),
      });
      const data = await res.json();
      if (!res.ok) {
        setCancelError(data?.detail || data?.message || 'Failed to cancel subscription');
        setCancelling(false);
        return;
      }
      setShowCancelConfirm(false);
      refetchBilling();
    } catch {
      setCancelError('Network error. Please try again.');
    } finally {
      setCancelling(false);
    }
  };

  return (
    <div class="p-8 max-w-3xl">
      <h1 class="text-2xl font-bold text-xcord-text-primary mb-8">Billing</h1>

      {/* Loading / error state */}
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
          <>
            {/* Current Plan */}
            <div class="bg-xcord-bg-secondary rounded-lg p-6 mb-6">
              <div class="flex items-center justify-between mb-4">
                <h2 class="text-lg font-semibold text-xcord-text-primary">Current Plan</h2>
                <div class="flex items-center gap-2">
                  <span class={`px-3 py-1 text-sm font-medium rounded ${statusBadge(data().status)}`}>
                    {data().status}
                  </span>
                  <span class="px-3 py-1 bg-xcord-brand/10 text-xcord-brand text-sm font-medium rounded">
                    {data().tier}
                  </span>
                </div>
              </div>

              <div class="grid grid-cols-3 gap-4 mb-4">
                <div>
                  <div class="text-xs text-xcord-text-muted mb-1">Instances</div>
                  <div class="text-xcord-text-primary font-medium">
                    {data().instanceCount} / {formatInstances(data().maxInstances)}
                  </div>
                </div>
                <div>
                  <div class="text-xs text-xcord-text-muted mb-1">Members per Instance</div>
                  <div class="text-xcord-text-primary font-medium">
                    {formatUsers(data().currentTierInfo.maxUsersPerInstance)}
                  </div>
                </div>
                <div>
                  <div class="text-xs text-xcord-text-muted mb-1">Storage</div>
                  <div class="text-xcord-text-primary font-medium">
                    {formatStorage(data().currentTierInfo.maxStorageMb)}
                  </div>
                </div>
              </div>

              <Show when={data().currentPeriodEnd}>
                <div class="text-xs text-xcord-text-muted mb-3">
                  Billing period ends {formatDate(data().currentPeriodEnd!)}
                </div>
              </Show>

              <div class="pt-4 border-t border-xcord-bg-tertiary">
                <A href="/pricing" class="text-sm text-xcord-text-link hover:underline">
                  Compare plans &rarr;
                </A>
              </div>
            </div>

            {/* Upgrade / downgrade */}
            <div class="bg-xcord-bg-secondary rounded-lg p-6 mb-6">
              <h2 class="text-lg font-semibold text-xcord-text-primary mb-4">Change Plan</h2>

              <Show when={upgradeError()}>
                <div class="mb-4 text-sm text-xcord-red bg-xcord-red/10 rounded p-3">{upgradeError()}</div>
              </Show>
              <Show when={upgradeSuccess()}>
                <div class="mb-4 text-sm text-xcord-green bg-xcord-green/10 rounded p-3">{upgradeSuccess()}</div>
              </Show>

              <div class="grid grid-cols-1 gap-3">
                <For each={data().availableTiers}>
                  {(tier) => {
                    const isCurrent = tier.name === data().tier;
                    const isLoading = upgrading() === tier.name;
                    return (
                      <div class={`flex items-center justify-between p-4 rounded-lg border ${
                        isCurrent
                          ? 'border-xcord-brand bg-xcord-brand/5'
                          : 'border-xcord-bg-tertiary'
                      }`}>
                        <div>
                          <div class="flex items-center gap-2">
                            <span class="font-medium text-xcord-text-primary">{tier.name}</span>
                            <Show when={isCurrent}>
                              <span class="text-xs px-2 py-0.5 bg-xcord-brand/20 text-xcord-brand rounded">
                                Current
                              </span>
                            </Show>
                          </div>
                          <div class="text-sm text-xcord-text-muted mt-0.5">
                            {tier.price}{tier.period}
                            {' — '}
                            {formatInstances(tier.maxInstances)} instance{tier.maxInstances !== 1 ? 's' : ''},
                            {' '}{formatStorage(tier.maxStorageMb)} storage
                          </div>
                        </div>
                        <Switch>
                          <Match when={isCurrent}>
                            <span class="text-xs text-xcord-text-muted">Active</span>
                          </Match>
                          <Match when={tier.name === 'Enterprise'}>
                            <button
                              onClick={() => handleUpgrade('Enterprise')}
                              disabled={isLoading}
                              class="px-4 py-2 text-sm font-medium bg-xcord-bg-accent hover:bg-xcord-bg-tertiary text-xcord-text-primary rounded transition disabled:opacity-50"
                            >
                              {isLoading ? 'Loading...' : 'Contact Sales'}
                            </button>
                          </Match>
                          <Match when={true}>
                            <button
                              onClick={() => handleUpgrade(tier.name)}
                              disabled={!!upgrading()}
                              class="px-4 py-2 text-sm font-medium bg-xcord-brand hover:bg-xcord-brand-hover text-white rounded transition disabled:opacity-50"
                            >
                              {isLoading ? 'Loading...' : (
                                tier.name === 'Free' ? 'Downgrade' : 'Upgrade'
                              )}
                            </button>
                          </Match>
                        </Switch>
                      </div>
                    );
                  }}
                </For>
              </div>
            </div>

            {/* Cancel subscription */}
            <Show when={data().tier !== 'Free'}>
              <div class="bg-xcord-bg-secondary rounded-lg p-6 mb-6 border border-xcord-red/20">
                <h2 class="text-lg font-semibold text-xcord-text-primary mb-2">Cancel Subscription</h2>
                <p class="text-sm text-xcord-text-muted mb-4">
                  Cancelling will immediately downgrade your account to the Free plan.
                  Instances exceeding Free tier limits will be suspended.
                </p>

                <Show when={cancelError()}>
                  <div class="mb-3 text-sm text-xcord-red bg-xcord-red/10 rounded p-3">{cancelError()}</div>
                </Show>

                <Show when={!showCancelConfirm()}>
                  <button
                    onClick={() => setShowCancelConfirm(true)}
                    class="px-4 py-2 text-sm font-medium text-xcord-red bg-xcord-red/10 hover:bg-xcord-red/20 rounded transition"
                  >
                    Cancel Subscription
                  </button>
                </Show>

                <Show when={showCancelConfirm()}>
                  <div class="space-y-3">
                    <p class="text-sm font-medium text-xcord-text-primary">
                      Are you sure? This action cannot be undone.
                    </p>
                    <div class="flex gap-3">
                      <button
                        onClick={handleCancel}
                        disabled={cancelling()}
                        class="px-4 py-2 text-sm font-medium bg-xcord-red text-white hover:opacity-80 disabled:opacity-50 rounded transition"
                      >
                        {cancelling() ? 'Cancelling...' : 'Yes, Cancel Subscription'}
                      </button>
                      <button
                        onClick={() => { setShowCancelConfirm(false); setCancelError(null); }}
                        class="px-4 py-2 text-sm bg-xcord-bg-accent text-xcord-text-secondary rounded transition"
                      >
                        Keep Plan
                      </button>
                    </div>
                  </div>
                </Show>
              </div>
            </Show>
          </>
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
