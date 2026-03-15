import { A } from '@solidjs/router';
import { createResource, createSignal, For, Show } from 'solid-js';
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

function formatTier(tier: string, media: boolean): string {
  return media ? `${tier} + Media` : tier;
}

function PlanEditor(props: {
  instance: InstanceBillingItem;
  onClose: () => void;
}) {
  const [showContact, setShowContact] = createSignal(false);

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
              <button
                type="button"
                class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center ring-2 ring-xcord-brand"
              >
                <div class="font-semibold">Free</div>
                <div class="text-xs text-xcord-text-muted mt-1">Up to 10 members</div>
              </button>
              <button
                type="button"
                class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center opacity-60 cursor-default"
              >
                <div class="font-semibold">Basic</div>
                <div class="text-xs text-xcord-brand mt-1">Coming soon</div>
              </button>
              <button
                type="button"
                class="px-3 py-3 rounded bg-xcord-bg-tertiary text-xcord-text-primary text-sm font-medium text-center opacity-60 cursor-default"
              >
                <div class="font-semibold">Pro</div>
                <div class="text-xs text-xcord-brand mt-1">Coming soon</div>
              </button>
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
                <span class="text-sm font-medium text-xcord-text-primary">Voice &amp; Video</span>
                <span class="text-xs text-xcord-brand ml-2">Coming soon</span>
                <div class="text-xs text-xcord-text-muted mt-0.5">
                  Voice channels, video calls, screen share
                </div>
              </div>
            </div>
          </div>

          {/* Price summary */}
          <div class="bg-xcord-bg-secondary rounded-lg p-4 mb-5">
            <div class="flex items-center justify-between">
              <span class="text-xs font-medium text-xcord-text-primary">Total</span>
              <span class="text-sm font-bold text-xcord-text-primary">Free</span>
            </div>
          </div>

          {/* Actions */}
          <div class="flex gap-3 justify-end">
            <button
              onClick={props.onClose}
              class="px-4 py-2 text-sm text-xcord-text-muted hover:text-xcord-text-primary transition"
            >
              Close
            </button>
          </div>
        </div>
      </div>

      <ContactModal open={showContact()} onClose={() => setShowContact(false)} />
    </div>
  );
}

export default function Billing() {
  const [billing] = createResource(fetchBilling);
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
