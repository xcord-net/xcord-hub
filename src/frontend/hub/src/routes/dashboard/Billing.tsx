import { A } from '@solidjs/router';
import { createResource, For, Show } from 'solid-js';

interface InstanceBillingItem {
  instanceId: string;
  domain: string;
  displayName: string;
  featureTier: string;
  userCountTier: string;
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

function formatFeature(tier: string): string {
  const map: Record<string, string> = {
    Chat: 'Chat',
    Audio: 'Chat + Audio',
    Video: 'Chat + Audio + Video',
  };
  return map[tier] ?? tier;
}

export default function Billing() {
  const [billing] = createResource(fetchBilling);
  const [invoices] = createResource(fetchInvoices);

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
                            {formatFeature(instance.featureTier)}
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

                      <Show when={instance.priceCents > 0}>
                        <div class="pt-3 border-t border-xcord-bg-tertiary">
                          <button
                            disabled
                            class="px-3 py-1.5 text-xs font-medium text-xcord-red bg-xcord-red/10 rounded opacity-50 cursor-not-allowed"
                            title="Coming soon"
                          >
                            Cancel
                          </button>
                        </div>
                      </Show>
                    </div>
                  )}
                </For>
              </div>
            </Show>
          </div>
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
